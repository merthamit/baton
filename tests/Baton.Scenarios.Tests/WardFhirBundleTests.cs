#nullable disable warnings
using Hl7.Fhir.Model;

namespace Baton.Scenarios.Tests;

public class WardFhirBundleTests
{
    private static readonly WardBed Bed = Ward.Generate(CardFidelityTests.Quiet.ShiftStart, 42)[1];

    [Fact]
    public void Bundle_IsATransactionWithThePatientAndEveryObservation()
    {
        var bundle = WardFhirBundle.Build(Bed);

        Assert.Equal(Bundle.BundleType.Transaction, bundle.Type);
        Assert.Single(bundle.Entry, e => e.Resource is Patient);
        Assert.Equal(Bed.Patient.Measurements.Count * 7, bundle.Entry.Count(e => e.Resource is Observation o && !o.Id!.StartsWith("news2-", StringComparison.Ordinal)));
    }

    [Fact]
    public void EveryEntry_IsAPutToItsOwnId_SoReloadingNeverDuplicates()
    {
        var bundle = WardFhirBundle.Build(Bed);

        Assert.All(bundle.Entry, e =>
        {
            Assert.Equal(Bundle.HTTPVerb.PUT, e.Request!.Method);
            Assert.Equal($"{e.Resource!.TypeName}/{e.Resource.Id}", e.Request.Url);
        });
        Assert.Equal(bundle.Entry.Count, bundle.Entry.Select(e => e.Request!.Url).Distinct().Count());
        Assert.Equal(
            WardFhirBundle.Build(Bed).Entry.Select(e => e.Request!.Url),
            bundle.Entry.Select(e => e.Request!.Url));
    }

    [Fact]
    public void Patient_CarriesBedNameAndSyntheticTag()
    {
        var patient = (Patient)WardFhirBundle.Build(Bed).Entry.Single(e => e.Resource is Patient).Resource!;

        Assert.Equal(Bed.PatientId, patient.Id);
        var bed = Assert.Single(patient.Identifier, i => i.System == WardFhirBundle.BedSystem);
        Assert.Equal("02", bed.Value);
        Assert.Equal("Ayşe K.", patient.Name.Single().Text);
        Assert.Contains(patient.Meta!.Tag, t => t.Code == "synthetic");
        Assert.Null(patient.GetExtension(WardFhirBundle.Scale2Extension));
    }

    [Fact]
    public void Scale2Patient_CarriesTheRecordedDecision()
    {
        var copd = Ward.Generate(CardFidelityTests.Quiet.ShiftStart, 42).Single(b => b.Bed == "05");
        var patient = (Patient)WardFhirBundle.Build(copd).Entry.Single(e => e.Resource is Patient).Resource!;

        var decision = patient.GetExtension(WardFhirBundle.Scale2Extension);
        Assert.NotNull(decision);
        Assert.False(string.IsNullOrWhiteSpace(decision.GetStringExtension("authorisedBy")));
        Assert.Equal(CardFidelityTests.Quiet.ShiftStart.AddMinutes(-30), new FhirDateTime(((FhirDateTime)decision.GetExtension("authorisedAt").Value).Value).ToDateTimeOffset(TimeSpan.Zero));
    }

    [Fact]
    public void Bundle_Bed02_HasFlagTaskProvenanceAndNews2()
    {
        var resources = WardFhirBundle.Build(Bed).Entry.Select(e => e.Resource).ToList();

        Assert.Single(resources.OfType<Flag>());
        var tasks = resources.OfType<Hl7.Fhir.Model.Task>().ToList();
        Assert.Equal(2, tasks.Count);
        Assert.All(resources.Where(r => r is Flag or Hl7.Fhir.Model.Task), r =>
            Assert.Contains(resources.OfType<Provenance>(), p => p.Target.Any(t => t.Reference == $"{r.TypeName}/{r.Id}")));

        var focus = tasks[0].Focus!.Reference!;
        Assert.Contains(resources.OfType<Observation>(), o => $"Observation/{o.Id}" == focus);
    }

    [Fact]
    public void Bundle_Practitioners_Present()
    {
        var ids = WardFhirBundle.Build(Bed).Entry.Select(e => e.Resource).OfType<Practitioner>().Select(p => p.Id).ToList();

        Assert.Equal(["synthetic-nurse", "synthetic-oncall", "synthetic-senior", "synthetic-cct"], ids);
    }

    [Fact]
    public void Loader_DeletesEscalationKindsBeforeObservations()
    {
        Assert.Equal(["Provenance", "Task", "Flag", "Communication", "Observation"], WardFhirLoader.DeleteOrder);
    }

    [Fact]
    public void Loader_DeletesLaterCallsFirst_BecauseTheyArePartOfEarlierOnes()
    {
        // A later call's Task points at the earlier one (partOf); a server that checks references refuses the other order.
        Assert.Equal(
            ["Task/call-x-10", "Task/call-x-2", "Task/call-x-1"],
            WardFhirLoader.TaskDeletionOrder(["Task/call-x-1", "Task/call-x-10", "Task/call-x-2"]));
    }

    [Fact]
    public void Loader_DeletesNews2ObservationsBeforeTheReadingsTheyComeFrom()
    {
        Assert.Equal(
            ["news2-synthetic-02-202611232315", "synthetic-02-0915-rr", "synthetic-02-0915-hr"],
            WardFhirLoader.ObservationDeletionOrder(["synthetic-02-0915-rr", "news2-synthetic-02-202611232315", "synthetic-02-0915-hr"]));
    }
}
