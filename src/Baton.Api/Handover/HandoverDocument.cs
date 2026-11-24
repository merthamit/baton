using System.Globalization;
using System.Net;
using System.Text;
using Baton.Core.Escalation;
using Baton.Fhir;
using Baton.Scenarios;
using Hl7.Fhir.Model;

namespace Baton.Api.Handover;

/// <summary>What the record held at the handover moment, for the document's included resources.</summary>
public sealed record HandoverSnapshot(
    PatientRef Patient,
    IReadOnlyList<Observation> Observations,
    IReadOnlyList<EscalationRecord> Records,
    IReadOnlyList<ScoreMark> Scores,
    EscalationOptions Options);

/// <summary>
/// A confirmed SBAR handover as a FHIR R4 document: a <c>Bundle</c> of type document whose first entry is the
/// <see cref="Composition"/>, followed by every resource it references, as they stood at the handover moment. The
/// outgoing nurse is the author, the incoming nurse the attester; Baton is recorded as the draft's assembler in a
/// <see cref="Provenance"/> kept beside the document.
/// </summary>
public static class HandoverDocument
{
    public const string IdentifierSystem = "urn:baton:handover";
    public const string Author = "Practitioner/synthetic-nurse";
    public const string Attester = "Practitioner/synthetic-incoming";

    /// <summary>A reserved domain: relative references resolve inside the document, nothing real is named or fetched.</summary>
    public const string FullUrlBase = "https://fhir.baton.invalid/";

    private const string FamilyId = "synthetic-family";

    public static string IdFor(string patientId, DateTimeOffset at) =>
        $"handover-{patientId}-{at.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}";

    public static Bundle Build(Sbar sbar, HandoverSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(sbar);
        ArgumentNullException.ThrowIfNull(snapshot);
        var patientId = snapshot.Patient.Id;
        var id = IdFor(patientId, sbar.At);
        var composition = Composition(sbar, snapshot.Patient, id);

        var pool = Pool(sbar, snapshot);
        var included = new List<Resource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(References(composition));
        while (queue.TryDequeue(out var reference))
        {
            if (!seen.Add(reference) || !pool.TryGetValue(reference, out var resource))
            {
                continue;
            }

            included.Add(resource);
            foreach (var next in References(resource))
            {
                queue.Enqueue(next);
            }
        }

        return new Bundle
        {
            Id = id,
            Meta = Synthetic(),
            Identifier = new Identifier(IdentifierSystem, id),
            Type = Bundle.BundleType.Document,
            Timestamp = sbar.At,
            Entry = new Resource[] { composition }.Concat(included)
                .Select(r => new Bundle.EntryComponent { FullUrl = $"{FullUrlBase}{r.TypeName}/{r.Id}", Resource = r })
                .ToList(),
        };
    }

    public static Provenance ProvenanceFor(Bundle document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Provenance
        {
            Id = $"prov-bundle-{document.Id}",
            Meta = Synthetic(),
            Target = [new ResourceReference($"Bundle/{document.Id}")],
            Recorded = document.Timestamp,
            Activity = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v3-DataOperation", "CREATE", "create"),
            Agent =
            [
                new Provenance.AgentComponent
                {
                    Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/provenance-participant-type", "assembler", "Assembler"),
                    Who = new ResourceReference($"Device/{EscalationFhir.BatonDevice.Id}"),
                },
            ],
        };
    }

    private static Composition Composition(Sbar sbar, PatientRef patient, string id)
    {
        var tr = sbar.Language == "tr";
        var sections = sbar.Sections
            .Select(s => (Title: Title(s.Part, tr), Texts: s.Sentences.Select(x => x.Text).ToList(), Sources: s.Sentences.SelectMany(x => x.Sources)))
            .ToList();
        if (sbar.Missing.Count > 0)
        {
            sections.Add((tr ? "Eksik" : "Missing", sbar.Missing.Select(m => m.Text).ToList(), sbar.Missing.SelectMany(m => m.Sources)));
        }

        var composition = new Composition
        {
            Id = id,
            Meta = Synthetic(),
            Language = sbar.Language,
            Status = CompositionStatus.Final,
            Type = new CodeableConcept("http://loinc.org", "28651-8", "Nurse Transfer note"),
            Subject = new ResourceReference($"Patient/{patient.Id}"),
            Date = Time(sbar.At),
            Author = [new ResourceReference(Author)],
            Title = tr ? $"SBAR devri · {patient.Name}" : $"SBAR handover · {patient.Name}",
            Attester =
            [
                new Composition.AttesterComponent
                {
                    Mode = Hl7.Fhir.Model.Composition.CompositionAttestationMode.Professional,
                    Time = Time(sbar.At),
                    Party = new ResourceReference(Attester),
                },
            ],
            Section = sections.Select(s => new Composition.SectionComponent
            {
                Title = s.Title,
                Text = Narrative(List(s.Texts)),
                Entry = s.Sources.Select(x => x.Reference).Distinct(StringComparer.Ordinal).Select(r => new ResourceReference(r)).ToList(),
            }).ToList(),
        };
        composition.Text = Narrative(string.Concat(sections.Select(s => $"<p><b>{WebUtility.HtmlEncode(s.Title)}</b></p>{List(s.Texts)}")));
        return composition;
    }

    private static string Title(SbarPart part, bool tr) => (part, tr) switch
    {
        (SbarPart.Situation, false) => "Situation",
        (SbarPart.Background, false) => "Background",
        (SbarPart.Assessment, false) => "Assessment",
        (SbarPart.Recommendation, false) => "Recommendation",
        (SbarPart.Situation, true) => "Durum",
        (SbarPart.Background, true) => "Arka plan",
        (SbarPart.Assessment, true) => "Değerlendirme",
        _ => "Öneri",
    };

    private static string List(IEnumerable<string> texts)
    {
        var html = new StringBuilder("<ul>");
        foreach (var text in texts)
        {
            html.Append("<li>").Append(WebUtility.HtmlEncode(text)).Append("</li>");
        }

        return html.Append("</ul>").ToString();
    }

    private static Narrative Narrative(string inner) => new()
    {
        Status = Hl7.Fhir.Model.Narrative.NarrativeStatus.Generated,
        Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\">{inner}</div>",
    };

    /// <summary>Every resource the document may include, keyed by reference, rebuilt as at the handover moment.</summary>
    private static Dictionary<string, Resource> Pool(Sbar sbar, HandoverSnapshot snapshot)
    {
        var patient = snapshot.Patient;
        var at = sbar.At;
        var observations = snapshot.Observations.Where(o => WardService.TimeOf(o) is { } t && t <= at).ToList();
        var news2Prefix = $"news2-{patient.Id}-";

        ResourceReference? News2At(DateTimeOffset moment) => observations
            .Where(o => o.Id?.StartsWith(news2Prefix, StringComparison.Ordinal) == true && WardService.TimeOf(o) <= moment)
            .OrderBy(o => WardService.TimeOf(o))
            .Select(o => new ResourceReference($"Observation/{o.Id}"))
            .LastOrDefault();

        var chain = EscalationFhir.ToResources(
                snapshot.Records.Where(r => r.At <= at).ToList(),
                patient.Id,
                News2At,
                snapshot.Scores.Where(s => s.At <= at).ToList(),
                snapshot.Options)
            .Where(r => r is not Provenance);

        var resources = new List<Resource> { PatientResource(patient), ConditionResource(patient, sbar.Language) };
        resources.AddRange(observations);
        resources.AddRange(chain);
        resources.AddRange(EscalationFhir.Practitioners);
        resources.Add(new RelatedPerson
        {
            Id = FamilyId,
            Meta = Synthetic(),
            Patient = new ResourceReference($"Patient/{patient.Id}"),
            Name = [new HumanName { Text = "Family member (synthetic)" }],
        });

        return resources
            .Where(r => r.Id is not null)
            .GroupBy(r => $"{r.TypeName}/{r.Id}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    private static Patient PatientResource(PatientRef patient)
    {
        var resource = new Patient
        {
            Id = patient.Id,
            Meta = Synthetic(),
            Name = [new HumanName { Text = patient.Name }],
            BirthDate = patient.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (patient.Bed is not null)
        {
            resource.Identifier = [new Identifier(WardFhirBundle.BedSystem, patient.Bed)];
        }

        return resource;
    }

    private static Condition ConditionResource(PatientRef patient, string language) => new()
    {
        Id = $"condition-{patient.Id}",
        Meta = Synthetic(),
        ClinicalStatus = new CodeableConcept(WardFhirBundle.ConditionClinicalSystem, "active"),
        VerificationStatus = new CodeableConcept(WardFhirBundle.ConditionVerificationSystem, "confirmed"),
        Category = [new CodeableConcept(WardFhirBundle.ConditionCategorySystem, "encounter-diagnosis")],
        Code = new CodeableConcept { Text = patient.DiagnosisIn(language == "tr" ? "tr" : null) ?? "Not recorded" },
        Subject = new ResourceReference($"Patient/{patient.Id}"),
    };

    /// <summary>Every relative reference ("Type/id") anywhere inside a resource.</summary>
    private static IEnumerable<string> References(Base element)
    {
        foreach (var (_, value) in element.EnumerateElements())
        {
            var children = value switch
            {
                Base single => [single],
                IEnumerable<Base> many => many,
                _ => [],
            };
            foreach (var child in children)
            {
                if (child is ResourceReference { Reference: { } reference } && reference.Contains('/', StringComparison.Ordinal) && !reference.StartsWith('#'))
                {
                    yield return reference;
                }

                foreach (var nested in References(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static Meta Synthetic() => new() { Tag = [new Coding(EscalationFhir.TagSystem, "synthetic", "Synthetic data generated by Baton")] };

    private static string Time(DateTimeOffset at) => at.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);
}
