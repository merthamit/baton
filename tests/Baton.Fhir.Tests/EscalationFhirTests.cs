using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Tests.Shared;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Baton.Fhir.Tests;

// The escalation chain in FHIR R4: concern → Flag, patient/family concern → Communication, each call → Task,
// and a Provenance for every resource. Records must survive the round trip whatever order the resources come in.
public class EscalationFhirTests
{
    private const string PatientId = "synthetic-02";
    private const string Nurse = "Practitioner/synthetic-nurse";

    private static readonly DateTimeOffset Day = new(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(3));

    private static DateTimeOffset T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

    private static readonly ScoreMark[] Scores = [new(T(23, 15), RiskLevel.Medium), new(T(24, 15), RiskLevel.High)];

    private static readonly EscalationRecord[] Story =
    [
        new ConcernRaised(T(20, 15), Nurse, "Restless, not eating, not herself"),
        new PatientOrFamilyConcern(T(21, 0), "RelatedPerson/synthetic-daughter", "She is not herself today"),
        new Called(T(23, 19), Nurse, "call-synthetic-02-1", EscalationTier.OnCallDoctor),
        new Arrived(T(23, 40), "Practitioner/synthetic-oncall", "call-synthetic-02-1", Simulated: false),
        new Assessed(T(23, 40), "Practitioner/synthetic-oncall", "call-synthetic-02-1", "On-call doctor: suspected sepsis"),
        new Planned(T(24, 5), "Practitioner/synthetic-oncall", "call-synthetic-02-1", "Cultures, fluids, antibiotics"),
        new Called(T(24, 20), Nurse, "call-synthetic-02-2", EscalationTier.CriticalCare),
        new Arrived(T(24, 30), "Practitioner/synthetic-cct", "call-synthetic-02-2", Simulated: true),
    ];

    private static ResourceReference? News2At(DateTimeOffset at) => new($"Observation/news2-{PatientId}-{at:yyyyMMddHHmm}");

    private static IReadOnlyList<Resource> Resources(Func<DateTimeOffset, ResourceReference?>? news2 = null) =>
        EscalationFhir.ToResources(Story, PatientId, news2 ?? News2At, Scores);

    [Fact]
    public void RoundTrip_Records_Equal()
    {
        var shuffled = Resources().Reverse();

        var back = EscalationFhir.FromResources(shuffled).OrderBy(r => r.At).ThenBy(r => r.GetType().Name).ToList();

        Assert.Equal(Story.OrderBy(r => r.At).ThenBy(r => r.GetType().Name), back);
    }

    [Fact]
    public void Task_HasTargetAndFocus_WhenScoreKnown()
    {
        var task = Resources().OfType<Hl7.Fhir.Model.Task>().Single(t => t.Id == "call-synthetic-02-1");

        Assert.Equal($"Observation/news2-{PatientId}-202611232319", task.Focus!.Reference);
        Assert.Equal(T(24, 19), DateTimeOffset.Parse(task.Restriction!.Period!.End!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Hl7.Fhir.Model.Task.TaskStatus.Completed, task.Status);
        Assert.Equal("planned", task.BusinessStatus!.Text);
        Assert.Equal($"Patient/{PatientId}", task.For!.Reference);
    }

    [Fact]
    public void Task_NoRestriction_WithoutFocus()
    {
        var task = Resources(_ => null).OfType<Hl7.Fhir.Model.Task>().First();

        Assert.Null(task.Focus);
        Assert.Null(task.Restriction);
    }

    [Fact]
    public void CriticalCareTask_PartOfOnCall_ArrivalSimulated()
    {
        var task = Resources().OfType<Hl7.Fhir.Model.Task>().Single(t => t.Id == "call-synthetic-02-2");

        Assert.Equal("Task/call-synthetic-02-1", task.PartOf.Single().Reference);
        Assert.Equal(Hl7.Fhir.Model.Task.TaskStatus.InProgress, task.Status);
        Assert.Equal("arrived", task.BusinessStatus!.Text);
        Assert.Contains(task.Note, n => n.Text == EscalationFhir.ArrivalSimulatedNote);
    }

    [Fact]
    public void ConcernIsFlag_PatientConcernIsCommunication_EachHasProvenance()
    {
        var resources = Resources();

        var flag = resources.OfType<Flag>().Single();
        Assert.Equal("Restless, not eating, not herself", flag.Code!.Text);
        Assert.Equal(Nurse, flag.Author!.Reference);
        var communication = resources.OfType<Communication>().Single();
        Assert.Equal("RelatedPerson/synthetic-daughter", communication.Sender!.Reference);

        var targets = resources.OfType<Provenance>().SelectMany(p => p.Target).Select(t => t.Reference).ToHashSet();
        Assert.All(resources.Where(r => r is not Provenance), r => Assert.Contains($"{r.TypeName}/{r.Id}", targets));
    }

    [Fact]
    public void AllResources_PassFirelyValidator()
    {
        var saved = new HashSet<string>();
        foreach (var resource in Resources().Concat(EscalationFhir.Practitioners))
        {
            var errors = FhirValidation.Validator.Validate(resource).Issue
                .Where(i => i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal)
                .Select(i => $"{resource.TypeName}/{resource.Id} {i.Severity}: {i.Details?.Text ?? i.Diagnostics} @ {string.Join(",", i.Expression)}")
                .ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

            if (saved.Add(resource.TypeName))
            {
                SaveExample(resource, $"escalation-{resource.TypeName.ToLowerInvariant()}");
            }
        }
    }

    [Fact]
    public void Practitioners_AreTaggedSynthetic()
    {
        Assert.Equal(4, EscalationFhir.Practitioners.Count);
        Assert.All(EscalationFhir.Practitioners, p => Assert.Contains(p.Meta!.Tag, t => t.Code == "synthetic"));
    }

    private static void SaveExample(Resource resource, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Baton.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts", "fhir-examples"));
        File.WriteAllText(Path.Combine(folder.FullName, $"{name}.json"), resource.ToJson(pretty: true));
    }
}
