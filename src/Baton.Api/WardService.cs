using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Fhir;
using Baton.Scenarios;
using Hl7.Fhir.Model;

namespace Baton.Api;

/// <summary>NEWS2 at one measurement time, as served by the API.</summary>
public sealed record ScorePoint(
    DateTimeOffset At,
    int? Total,
    int? MinimumTotal,
    RiskLevel? Risk,
    ClinicalResponse? Response,
    string? MinimumMonitoringFrequency,
    IReadOnlyDictionary<News2Parameter, int?> SubScores,
    IReadOnlyList<News2Parameter> ParametersScoring3,
    IReadOnlyList<News2Problem> Problems,
    News2Input Input,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Sources);

/// <summary>
/// One bed for the nurse card. <see cref="Drivers"/> are the parameters raising the latest score, highest sub-score
/// first (ties in NEWS2 chart order). <see cref="NextDue"/> follows <see cref="MonitoringSchedule"/>; without events
/// (<see cref="ConcernsKnown"/> false) it is computed from the score alone.
/// </summary>
public sealed record BedSummary(
    string PatientId,
    string Name,
    string? Bed,
    int? Age,
    string? Diagnosis,
    ScorePoint? Latest,
    ScorePoint? Previous,
    IReadOnlyList<News2Parameter> Drivers,
    DateTimeOffset? NextDue,
    int? OverdueMinutes,
    bool ConcernsKnown,
    EscalationState Escalation,
    bool EventsAvailable,
    IReadOnlyList<WardEvent> Events);

/// <summary>One escalation record for the patient's timeline: <c>Kind</c> is the record type (ConcernRaised, Called, …).</summary>
public sealed record EscalationRecordView(DateTimeOffset At, string Kind, string By, EscalationTier? Tier, string? Text, bool? Simulated)
{
    public static EscalationRecordView From(EscalationRecord record) => record switch
    {
        ConcernRaised c => new(c.At, nameof(ConcernRaised), c.By, null, c.Note, null),
        PatientOrFamilyConcern p => new(p.At, nameof(PatientOrFamilyConcern), p.By, null, p.Text, null),
        Called c => new(c.At, nameof(Called), c.By, c.Tier, null, null),
        Arrived a => new(a.At, nameof(Arrived), a.By, null, null, a.Simulated),
        Assessed a => new(a.At, nameof(Assessed), a.By, null, a.Text, null),
        Planned p => new(p.At, nameof(Planned), p.By, null, p.Text, null),
        _ => new(record.At, record.GetType().Name, record.By, null, null, null),
    };
}

public sealed record PatientSeries(
    PatientRef Patient,
    int? Age,
    string? Diagnosis,
    IReadOnlyList<ScorePoint> Points,
    bool EventsAvailable,
    IReadOnlyList<WardEvent> Events,
    EscalationState Escalation,
    IReadOnlyList<EscalationRecordView> EscalationRecords);

/// <summary>
/// Scores the ward from FHIR data. Observations are grouped by measurement time; each time is read with
/// <see cref="VitalSignsReader"/> (Now = that time, readings up to 13 hours old count, since a patient scoring 0
/// is measured 12-hourly) and scored with <see cref="News2Calculator"/>. No NEWS2 rule is repeated here.
/// </summary>
public sealed class WardService(IVitalSignsSource source, EscalationOptions? escalationOptions = null)
{
    private readonly IVitalSignsSource source = source;

    private readonly EscalationOptions options = escalationOptions ?? new EscalationOptions();

    public static readonly TimeSpan MaxReadingAge = TimeSpan.FromHours(13);

    public async Task<IReadOnlyList<BedSummary>> GetWardAsync(DateTimeOffset at, CancellationToken cancellationToken, string? language = null, IVitalSignsSource? overlay = null)
    {
        var source = overlay ?? this.source;
        var result = new List<BedSummary>();
        foreach (var patient in await source.GetPatientsAsync(cancellationToken))
        {
            var points = await ScoreAsync(source, patient, at, cancellationToken);
            var events = await source.GetEventsAsync(patient.Id, at, cancellationToken);
            var records = await source.GetEscalationAsync(patient.Id, at, language, cancellationToken);
            var latest = points.Count > 0 ? points[^1] : null;

            DateTimeOffset? nextDue = latest is null
                ? null
                : MonitoringSchedule.NextDue(
                    latest.At,
                    latest.Response,
                    records.OfType<ConcernRaised>().Select(c => c.At).ToList());
            int? overdue = nextDue is { } due ? (int)Math.Max(0, Math.Floor((at - due).TotalMinutes)) : null;

            result.Add(new BedSummary(
                patient.Id,
                patient.Name,
                patient.Bed,
                AgeAt(patient.BirthDate, at),
                patient.DiagnosisIn(language),
                latest,
                points.Count > 1 ? points[^2] : null,
                latest is null ? [] : DriversOf(latest.SubScores),
                nextDue,
                overdue,
                true,
                Chain(records, points, at),
                events.Available,
                NonChain(events.Events, language)));
        }

        return result;
    }

    /// <summary>Parameters with a sub-score above 0, highest first; ties keep the NEWS2 chart order.</summary>
    public static IReadOnlyList<News2Parameter> DriversOf(IReadOnlyDictionary<News2Parameter, int?> subScores)
    {
        ArgumentNullException.ThrowIfNull(subScores);
        return subScores
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key)
            .Select(s => s.Key)
            .ToList();
    }

    /// <summary>Whole years from the birth date to the date of <paramref name="at"/> (in its own offset).</summary>
    public static int? AgeAt(DateOnly? birthDate, DateTimeOffset at)
    {
        if (birthDate is not { } born)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(at.DateTime);
        var age = today.Year - born.Year;
        return born.AddYears(age) > today ? age - 1 : age;
    }

    public async Task<PatientSeries?> GetPatientAsync(string patientId, DateTimeOffset at, CancellationToken cancellationToken, string? language = null, IVitalSignsSource? overlay = null)
    {
        var source = overlay ?? this.source;
        var patient = (await source.GetPatientsAsync(cancellationToken)).FirstOrDefault(p => p.Id == patientId);
        if (patient is null)
        {
            return null;
        }

        var events = await source.GetEventsAsync(patient.Id, at, cancellationToken);
        var records = await source.GetEscalationAsync(patient.Id, at, language, cancellationToken);
        var points = await ScoreAsync(source, patient, at, cancellationToken);
        return new PatientSeries(
            patient,
            AgeAt(patient.BirthDate, at),
            patient.DiagnosisIn(language),
            points,
            events.Available,
            NonChain(events.Events, language),
            Chain(records, points, at),
            records.OrderBy(r => r.At).Select(EscalationRecordView.From).ToList());
    }

    /// <summary>Scenario events that are now escalation records (shown from FHIR), so the timeline does not repeat them.</summary>
    private static readonly HashSet<ScenarioEventKind> ChainKinds =
    [
        ScenarioEventKind.Concern,
        ScenarioEventKind.DoctorCalled,
        ScenarioEventKind.DoctorArrived,
        ScenarioEventKind.TreatmentStarted,
        ScenarioEventKind.CriticalCareCalled,
    ];

    /// <summary>Non-chain events; generator texts in Turkish when asked (they are not in FHIR yet).</summary>
    private static IReadOnlyList<WardEvent> NonChain(IReadOnlyList<WardEvent> events, string? language) =>
        events
            .Where(e => !ChainKinds.Contains(e.Kind))
            .Select(e => language == "tr" && ScenarioTranslations.ToTurkish(e.Text) is { } tr ? e with { Text = tr } : e)
            .ToList();

    private EscalationState Chain(IReadOnlyList<EscalationRecord> records, IReadOnlyList<ScorePoint> points, DateTimeOffset at)
    {
        return EscalationChain.At(records, points.Select(p => new ScoreMark(p.At, p.Risk)).ToList(), at, options);
    }

    public static DateTimeOffset? TimeOf(Observation observation) =>
        observation.Effective is FhirDateTime dt && dt.TryToDateTimeOffset(out var value) ? value : null;

    /// <summary>
    /// The FHIR references behind a score: every reading that entered it (older readings carried forward included) and
    /// the NEWS2 Observation stored for that measurement time, when there is one.
    /// </summary>
    private static List<string> SourcesOf(string patientId, DateTimeOffset time, VitalSignsReading reading, IReadOnlyList<Observation> observations)
    {
        var news2Prefix = $"news2-{patientId}-";
        return reading.Sources.Values
            .Concat(observations.Where(o => o.Id?.StartsWith(news2Prefix, StringComparison.Ordinal) == true && TimeOf(o) == time))
            .Where(o => o.Id is not null)
            .Select(o => $"Observation/{o.Id}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<List<ScorePoint>> ScoreAsync(IVitalSignsSource source, PatientRef patient, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var observations = await source.GetObservationsAsync(patient.Id, at, cancellationToken);
        var times = observations
            .Select(TimeOf)
            .OfType<DateTimeOffset>()
            .Where(t => t <= at)
            .Select(t => t.ToUniversalTime())
            .Distinct()
            .Order()
            .ToList();

        return times.Select(time =>
        {
            var options = new ReadOptions(time, MaxReadingAge)
            {
                SpO2Scale = patient.SpO2Scale,
                Scale2Authorisation = patient.Scale2Authorisation,
            };
            var reading = VitalSignsReader.Read(observations.Where(o => TimeOf(o) is { } t && t <= time), options);
            var score = News2Calculator.Calculate(reading.Input);
            var local = time.ToOffset(at.Offset);
            return new ScorePoint(local, score.Total, score.MinimumTotal, score.Risk, score.Response, score.MinimumMonitoringFrequency,
                score.SubScores, score.ParametersScoring3, score.Problems, reading.Input, reading.Notes, SourcesOf(patient.Id, time, reading, observations));
        }).ToList();
    }
}
