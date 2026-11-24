using System.Globalization;
using Baton.Core.Escalation;
using Hl7.Fhir.Model;
using FhirTask = Hl7.Fhir.Model.Task;

namespace Baton.Fhir;

/// <summary>
/// The escalation chain in FHIR R4. A nurse's concern is a <see cref="Flag"/>; a patient's or family member's concern is a
/// <see cref="Communication"/> (an R4 Flag cannot have a RelatedPerson author); each call is a <see cref="FhirTask"/> whose
/// status follows the chain (requested → in-progress → completed), with the response target in
/// <c>restriction.period.end</c> (allowed only with a <c>focus</c>: the NEWS2 Observation behind the call). Every resource
/// gets a <see cref="Provenance"/> saying who created it and when.
/// </summary>
public static class EscalationFhir
{
    public const string System = "urn:baton:escalation";
    public const string TierSystem = "urn:baton:escalation-tier";
    public const string TagSystem = "urn:baton:tag";
    public const string ArrivalSimulatedNote = "Arrival simulated by Baton (no recorded arrival)";

    private const string AssessmentPrefix = "Assessment: ";
    private const string PlanPrefix = "Plan: ";

    /// <summary>The synthetic clinicians the scenario and the demo user act as.</summary>
    public static IReadOnlyList<Practitioner> Practitioners { get; } =
    [
        Clinician("synthetic-nurse", "Ward nurse (synthetic)"),
        Clinician("synthetic-oncall", "On-call doctor (synthetic)"),
        Clinician("synthetic-senior", "Senior doctor (synthetic)"),
        Clinician("synthetic-cct", "Critical care team (synthetic)"),
    ];

    public static IReadOnlyList<Resource> ToResources(
        IReadOnlyList<EscalationRecord> records,
        string patientId,
        Func<DateTimeOffset, ResourceReference?> news2At,
        IReadOnlyList<ScoreMark> scores,
        EscalationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrEmpty(patientId);
        ArgumentNullException.ThrowIfNull(news2At);
        ArgumentNullException.ThrowIfNull(scores);

        var ordered = records.OrderBy(r => r.At).ToList();
        var subject = new ResourceReference($"Patient/{patientId}");
        var resources = new List<Resource>();
        var provenance = new List<Resource>();

        foreach (var concern in ordered.OfType<ConcernRaised>())
        {
            var flag = new Flag
            {
                Id = $"concern-{patientId}-{Stamp(concern.At)}",
                Meta = Synthetic(),
                Status = Flag.FlagStatus.Active,
                Category = [new CodeableConcept("http://terminology.hl7.org/CodeSystem/flag-category", "clinical")],
                Code = new CodeableConcept(System, "nurse-concern", "Nurse concern", concern.Note),
                Subject = subject,
                Period = new Period { Start = Time(concern.At) },
                Author = new ResourceReference(concern.By),
            };
            resources.Add(flag);
            provenance.Add(Provenance(flag, concern.At, concern.By));
        }

        foreach (var voice in ordered.OfType<PatientOrFamilyConcern>())
        {
            var communication = new Communication
            {
                Id = $"voice-{patientId}-{Stamp(voice.At)}",
                Meta = Synthetic(),
                Status = EventStatus.Completed,
                Category = [new CodeableConcept(System, "patient-or-family-concern", "Patient or family concern")],
                Subject = subject,
                Sent = Time(voice.At),
                Sender = new ResourceReference(voice.By),
                Payload = [new Communication.PayloadComponent { Content = new FhirString(voice.Text) }],
            };
            resources.Add(communication);
            provenance.Add(Provenance(communication, voice.At, voice.By));
        }

        var horizon = ordered.Count == 0 ? DateTimeOffset.UnixEpoch : ordered[^1].At.AddDays(1);
        var state = EscalationChain.At(ordered, scores, horizon, options);
        string? previous = null;
        foreach (var called in ordered.OfType<Called>())
        {
            var call = state.Calls.Single(c => c.CallId == called.CallId);
            var task = CallTask(called, call, ordered, subject, news2At(called.At), previous);
            resources.Add(task);
            provenance.Add(Provenance(task, ordered.Where(r => CallIdOf(r) == called.CallId).Max(r => r.At), called.By));
            previous = called.CallId;
        }

        resources.AddRange(provenance);
        return resources;
    }

    public static IReadOnlyList<EscalationRecord> FromResources(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var records = new List<EscalationRecord>();

        foreach (var resource in resources)
        {
            switch (resource)
            {
                case Flag flag when Has(flag.Code, "nurse-concern") && Parse(flag.Period?.Start) is { } at:
                    records.Add(new ConcernRaised(at, flag.Author?.Reference ?? "", flag.Code!.Text ?? ""));
                    break;

                case Communication c when c.Category.Any(cat => Has(cat, "patient-or-family-concern")) && Parse(c.Sent) is { } sent:
                    records.Add(new PatientOrFamilyConcern(sent, c.Sender?.Reference ?? "", (c.Payload.FirstOrDefault()?.Content as FhirString)?.Value ?? ""));
                    break;

                case FhirTask task when Has(task.Code, "clinical-review"):
                    records.AddRange(FromTask(task));
                    break;
            }
        }

        return records;
    }

    private static FhirTask CallTask(Called called, CallState call, List<EscalationRecord> ordered, ResourceReference subject, ResourceReference? focus, string? previous)
    {
        var steps = ordered.Where(r => CallIdOf(r) == called.CallId).ToList();
        var arrived = steps.OfType<Arrived>().FirstOrDefault();
        var assessed = steps.OfType<Assessed>().FirstOrDefault();
        var planned = steps.OfType<Planned>().FirstOrDefault();

        var task = new FhirTask
        {
            Id = called.CallId,
            Meta = Synthetic(),
            Status = planned is not null ? FhirTask.TaskStatus.Completed
                : arrived is not null ? FhirTask.TaskStatus.InProgress
                : FhirTask.TaskStatus.Requested,
            BusinessStatus = new CodeableConcept { Text = planned is not null ? "planned" : assessed is not null ? "assessed" : arrived is not null ? "arrived" : "called" },
            Intent = FhirTask.TaskIntent.Order,
            Code = new CodeableConcept(System, "clinical-review", "Clinical review", "Clinical review"),
            For = subject,
            AuthoredOn = Time(called.At),
            Requester = new ResourceReference(called.By),
            PerformerType = [new CodeableConcept(TierSystem, called.Tier.ToString(), called.Tier.ToString())],
        };

        if (focus is not null)
        {
            task.Focus = focus;
            if (call.TargetAt is { } target)
            {
                task.Restriction = new FhirTask.RestrictionComponent { Period = new Period { End = Time(target) } };
            }
        }

        if (previous is not null)
        {
            task.PartOf = [new ResourceReference($"Task/{previous}")];
        }

        if (arrived is not null)
        {
            task.ExecutionPeriod = new Period { Start = Time(arrived.At) };
            task.Owner = new ResourceReference(arrived.By);
            if (arrived.Simulated)
            {
                task.Note.Add(new Annotation { Text = ArrivalSimulatedNote, Time = Time(arrived.At) });
            }
        }

        if (assessed is not null)
        {
            task.Note.Add(new Annotation { Text = AssessmentPrefix + assessed.Text, Time = Time(assessed.At), Author = new ResourceReference(assessed.By) });
        }

        if (planned is not null)
        {
            task.Note.Add(new Annotation { Text = PlanPrefix + planned.Text, Time = Time(planned.At), Author = new ResourceReference(planned.By) });
        }

        return task;
    }

    private static IEnumerable<EscalationRecord> FromTask(FhirTask task)
    {
        if (Parse(task.AuthoredOn) is not { } calledAt
            || !Enum.TryParse<EscalationTier>(task.PerformerType.FirstOrDefault()?.Coding.FirstOrDefault(c => c.System == TierSystem)?.Code, out var tier))
        {
            yield break;
        }

        var id = task.Id!;
        yield return new Called(calledAt, task.Requester?.Reference ?? "", id, tier);

        if (Parse(task.ExecutionPeriod?.Start) is { } arrivedAt)
        {
            var simulated = task.Note.Any(n => n.Text == ArrivalSimulatedNote);
            yield return new Arrived(arrivedAt, task.Owner?.Reference ?? "", id, simulated);
        }

        foreach (var note in task.Note)
        {
            var text = note.Text ?? "";
            var by = (note.Author as ResourceReference)?.Reference ?? "";
            if (Parse(note.Time) is not { } at)
            {
                continue;
            }

            if (text.StartsWith(AssessmentPrefix, StringComparison.Ordinal))
            {
                yield return new Assessed(at, by, id, text[AssessmentPrefix.Length..]);
            }
            else if (text.StartsWith(PlanPrefix, StringComparison.Ordinal))
            {
                yield return new Planned(at, by, id, text[PlanPrefix.Length..]);
            }
        }
    }

    private static Provenance Provenance(Resource target, DateTimeOffset recorded, string by) => new()
    {
        Id = $"prov-{target.TypeName.ToLowerInvariant()}-{target.Id}",
        Meta = Synthetic(),
        Target = [new ResourceReference($"{target.TypeName}/{target.Id}")],
        Recorded = recorded,
        Activity = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v3-DataOperation", "CREATE", "create"),
        Agent = [new Provenance.AgentComponent { Who = new ResourceReference(by) }],
    };

    private static Practitioner Clinician(string id, string name) => new()
    {
        Id = id,
        Meta = Synthetic(),
        Name = [new HumanName { Text = name }],
    };

    private static Meta Synthetic() => new() { Tag = [new Coding(TagSystem, "synthetic", "Synthetic data generated by Baton")] };

    private static string? CallIdOf(EscalationRecord record) => record switch
    {
        Called c => c.CallId,
        Arrived a => a.CallId,
        Assessed a => a.CallId,
        Planned p => p.CallId,
        _ => null,
    };

    private static bool Has(CodeableConcept? concept, string code) =>
        concept?.Coding.Any(c => c.System == System && c.Code == code) == true;

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at) => at.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
}
