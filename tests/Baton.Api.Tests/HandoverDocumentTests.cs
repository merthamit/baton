#nullable disable warnings
using Baton.Api.Handover;
using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Tests.Shared;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using static Baton.Api.Tests.SbarBuilderTests;

namespace Baton.Api.Tests;

// The confirmed handover as a FHIR document: Composition first, attested by the incoming nurse, every reference inside
// the Bundle, and nothing the record did not hold at the handover moment.
// Shares the Firely validator, whose first schema load is not safe to run in parallel.
[Collection("Firely validator")]
public class HandoverDocumentTests
{
    private static readonly TimeSpan Ward = TimeSpan.FromHours(3);
    private const string Pid = "synthetic-02";
    private const string Nurse = "Practitioner/synthetic-nurse";
    private const string Oncall = "Practitioner/synthetic-oncall";

    private static DateTimeOffset T(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, Ward);

    private static readonly PatientRef Patient = new(Pid, "Ayşe K.", "02", SpO2Scale.Scale1, null, new DateOnly(1955, 8, 15), "Urinary tract infection");

    private static Observation Vital(string id, DateTimeOffset at, string loinc, decimal value, string unit) => new()
    {
        Id = id,
        Status = ObservationStatus.Final,
        Category = [new CodeableConcept("http://terminology.hl7.org/CodeSystem/observation-category", "vital-signs")],
        Code = new CodeableConcept("http://loinc.org", loinc),
        Subject = new ResourceReference($"Patient/{Pid}"),
        Effective = new FhirDateTime(at),
        Value = new Quantity(value, unit),
    };

    private static IReadOnlyList<Observation> VitalsFor(params DateTimeOffset[] times) => times
        .SelectMany(at => new[]
        {
            Vital($"obs-{Pid}-{at:yyyyMMddHHmm}-rr", at, "9279-1", 22, "/min"),
            Vital($"obs-{Pid}-{at:yyyyMMddHHmm}-hr", at, "8867-4", 104, "/min"),
        })
        .ToList();

    private static readonly EscalationRecord[] Story =
    [
        new ConcernRaised(T(23, 20, 15), Nurse, "Restless, not eating"),
        new Called(T(24, 0), Nurse, "call-synthetic-02-1", EscalationTier.OnCallDoctor),
        new Arrived(T(24, 0, 25), Oncall, "call-synthetic-02-1", false),
        new Assessed(T(24, 0, 30), Oncall, "call-synthetic-02-1", "Suspected sepsis"),
        new Planned(T(24, 1), Oncall, "call-synthetic-02-1", "Cultures, fluids, antibiotics"),
    ];

    private static (Sbar Sbar, HandoverSnapshot Snapshot) Build(DateTimeOffset at, string? language = null)
    {
        var points = new[] { Point(T(23, 23, 50), 6, RiskLevel.Medium), Point(T(24, 7), 3, RiskLevel.Low) }.Where(p => p.At <= at).ToList();
        var records = Story.Where(r => r.At <= at).ToList();
        var sbar = SbarBuilder.Build(Input(points, records, at: at, nextDue: at.AddHours(1)), language);
        var snapshot = new HandoverSnapshot(
            Patient,
            VitalsFor(T(23, 23, 50), T(24, 7)),
            Story,
            points.Select(p => new ScoreMark(p.At, p.Risk)).ToList(),
            new EscalationOptions());
        return (sbar, snapshot);
    }

    private static Composition CompositionOf(Bundle bundle) => Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public void CompositionIsFirstAndAttestedByIncomingNurse()
    {
        var (sbar, snapshot) = Build(T(24, 8));
        var bundle = HandoverDocument.Build(sbar, snapshot);

        Assert.Equal(Bundle.BundleType.Document, bundle.Type);
        Assert.Equal(HandoverDocument.IdFor(Pid, T(24, 8)), bundle.Id);
        Assert.Equal("handover-synthetic-02-202611240800", bundle.Id);
        Assert.Equal(HandoverDocument.IdentifierSystem, bundle.Identifier.System);
        var composition = CompositionOf(bundle);
        Assert.Equal(CompositionStatus.Final, composition.Status);
        Assert.Equal("28651-8", composition.Type.Coding.Single().Code);
        Assert.Equal($"Patient/{Pid}", composition.Subject!.Reference);
        Assert.Equal(Nurse, composition.Author.Single().Reference);
        var attester = composition.Attester.Single();
        Assert.Equal(Composition.CompositionAttestationMode.Professional, attester.Mode);
        Assert.Equal("Practitioner/synthetic-incoming", attester.Party.Reference);
        Assert.Equal(T(24, 8), DateTimeOffset.Parse(attester.Time, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(T(24, 8), DateTimeOffset.Parse(composition.Date, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SectionsCarryExactlyTheShownSentences()
    {
        var (sbar, snapshot) = Build(T(24, 8));
        var composition = CompositionOf(HandoverDocument.Build(sbar, snapshot));

        foreach (var section in sbar.Sections)
        {
            var div = composition.Section[(int)section.Part].Text.Div;
            var items = System.Text.RegularExpressions.Regex.Matches(div, "<li>(.*?)</li>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
            Assert.Equal(section.Sentences.Select(s => s.Text), items);
        }
    }

    [Fact]
    public void EveryReferenceInTheCompositionIsInTheBundle()
    {
        var (sbar, snapshot) = Build(T(24, 8));
        var bundle = HandoverDocument.Build(sbar, snapshot);
        var inside = bundle.Entry.Select(e => $"{e.Resource.TypeName}/{e.Resource.Id}").ToHashSet(StringComparer.Ordinal);
        var composition = CompositionOf(bundle);

        var referenced = composition.Section.SelectMany(s => s.Entry).Select(r => r.Reference)
            .Append(composition.Subject!.Reference)
            .Concat(composition.Author.Select(a => a.Reference))
            .Concat(composition.Attester.Select(a => a.Party.Reference));
        Assert.All(referenced, r => Assert.Contains(r, inside));
        Assert.All(bundle.Entry, e => Assert.Equal($"https://fhir.baton.invalid/{e.Resource.TypeName}/{e.Resource.Id}", e.FullUrl));
    }

    [Fact]
    public void SnapshotHasNoLaterFacts()
    {
        // At 00:40 the doctor has come and assessed, but the plan (01:00) has not been made yet.
        var (sbar, snapshot) = Build(T(24, 0, 40));
        var task = HandoverDocument.Build(sbar, snapshot).Entry.Select(e => e.Resource).OfType<Hl7.Fhir.Model.Task>().Single();

        Assert.Equal(Hl7.Fhir.Model.Task.TaskStatus.InProgress, task.Status);
        Assert.DoesNotContain(task.Note, n => n.Text.StartsWith("Plan", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentPassesFirelyValidation()
    {
        var (sbar, snapshot) = Build(T(24, 8));
        var bundle = HandoverDocument.Build(sbar, snapshot);
        var provenance = HandoverDocument.ProvenanceFor(bundle);

        foreach (Resource resource in new Resource[] { bundle, provenance })
        {
            var errors = FhirValidation.Validator.Validate(resource).Issue
                .Where(i => i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal)
                .Select(i => $"{i.Expression.FirstOrDefault()}: {i.Details?.Text ?? i.Diagnostics}")
                .ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        Assert.Equal($"Bundle/{bundle.Id}", provenance.Target.Single().Reference);
        Assert.Equal("Device/baton", provenance.Agent.Single().Who.Reference);
    }

    [Fact]
    public void TurkishDocumentHasLanguageTr()
    {
        var (sbar, snapshot) = Build(T(24, 8), "tr");
        var composition = CompositionOf(HandoverDocument.Build(sbar, snapshot));

        Assert.Equal("tr", composition.Language);
        Assert.Equal("Durum", composition.Section[0].Title);
    }

    // CI runs the official HL7 validator on artifacts/fhir-examples.
    internal static void WriteExample(Bundle bundle)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Baton.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts", "fhir-examples"));
        File.WriteAllText(Path.Combine(folder.FullName, "handover-document.json"), bundle.ToJson(pretty: true));
    }
}
