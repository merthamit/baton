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
        Assert.Equal(Bed.Patient.Measurements.Count * 7, bundle.Entry.Count(e => e.Resource is Observation));
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
        Assert.Equal("Y-02", bed.Value);
        Assert.Equal("Ayşe K.", patient.Name.Single().Text);
        Assert.Contains(patient.Meta!.Tag, t => t.Code == "synthetic");
        Assert.Null(patient.GetExtension(WardFhirBundle.Scale2Extension));
    }

    [Fact]
    public void Scale2Patient_CarriesTheRecordedDecision()
    {
        var copd = Ward.Generate(CardFidelityTests.Quiet.ShiftStart, 42).Single(b => b.Bed == "Y-05");
        var patient = (Patient)WardFhirBundle.Build(copd).Entry.Single(e => e.Resource is Patient).Resource!;

        var decision = patient.GetExtension(WardFhirBundle.Scale2Extension);
        Assert.NotNull(decision);
        Assert.False(string.IsNullOrWhiteSpace(decision.GetStringExtension("authorisedBy")));
        Assert.Equal(CardFidelityTests.Quiet.ShiftStart.AddMinutes(-30), new FhirDateTime(((FhirDateTime)decision.GetExtension("authorisedAt").Value).Value).ToDateTimeOffset(TimeSpan.Zero));
    }
}
