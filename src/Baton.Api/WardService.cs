using Baton.Core.News2;
using Baton.Fhir;
using Baton.Scenarios;
using Hl7.Fhir.Model;

namespace Baton.Api;

/// <summary>NEWS2 at one measurement time, as served by the API.</summary>
public sealed record ScorePoint(
    DateTimeOffset At,
    int? Total,
    RiskLevel? Risk,
    ClinicalResponse? Response,
    string? MinimumMonitoringFrequency,
    IReadOnlyDictionary<News2Parameter, int?> SubScores,
    IReadOnlyList<News2Parameter> ParametersScoring3,
    IReadOnlyList<News2Problem> Problems,
    News2Input Input,
    IReadOnlyList<string> Notes);

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
    Escalation Escalation,
    bool EventsAvailable,
    IReadOnlyList<WardEvent> Events);

public sealed record PatientSeries(
    PatientRef Patient,
    int? Age,
    string? Diagnosis,
    IReadOnlyList<ScorePoint> Points,
    bool EventsAvailable,
    IReadOnlyList<WardEvent> Events);

/// <summary>
/// Scores the ward from FHIR data. Observations are grouped by measurement time; each time is read with
/// <see cref="VitalSignsReader"/> (Now = that time, readings up to 13 hours old count, since a patient scoring 0
/// is measured 12-hourly) and scored with <see cref="News2Calculator"/>. No NEWS2 rule is repeated here.
/// </summary>
public sealed class WardService(IVitalSignsSource source)
{
    public static readonly TimeSpan MaxReadingAge = TimeSpan.FromHours(13);

    public async Task<IReadOnlyList<BedSummary>> GetWardAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var result = new List<BedSummary>();
        foreach (var patient in await source.GetPatientsAsync(cancellationToken))
        {
            var points = await ScoreAsync(patient, at, cancellationToken);
            var events = await source.GetEventsAsync(patient.Id, at, cancellationToken);
            var latest = points.Count > 0 ? points[^1] : null;

            DateTimeOffset? nextDue = latest is null
                ? null
                : MonitoringSchedule.NextDue(
                    latest.At,
                    latest.Response,
                    events.Events.Where(e => e.Kind == ScenarioEventKind.Concern).Select(e => e.At).ToList());
            int? overdue = nextDue is { } due ? (int)Math.Max(0, Math.Floor((at - due).TotalMinutes)) : null;

            result.Add(new BedSummary(
                patient.Id,
                patient.Name,
                patient.Bed,
                AgeAt(patient.BirthDate, at),
                patient.Diagnosis,
                latest,
                points.Count > 1 ? points[^2] : null,
                latest is null ? [] : DriversOf(latest.SubScores),
                nextDue,
                overdue,
                events.Available,
                Escalation.From(events.Events, at),
                events.Available,
                events.Events));
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

    public async Task<PatientSeries?> GetPatientAsync(string patientId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var patient = (await source.GetPatientsAsync(cancellationToken)).FirstOrDefault(p => p.Id == patientId);
        if (patient is null)
        {
            return null;
        }

        var events = await source.GetEventsAsync(patient.Id, at, cancellationToken);
        return new PatientSeries(
            patient,
            AgeAt(patient.BirthDate, at),
            patient.Diagnosis,
            await ScoreAsync(patient, at, cancellationToken),
            events.Available,
            events.Events);
    }

    public static DateTimeOffset? TimeOf(Observation observation) =>
        observation.Effective is FhirDateTime dt && dt.TryToDateTimeOffset(out var value) ? value : null;

    private async Task<List<ScorePoint>> ScoreAsync(PatientRef patient, DateTimeOffset at, CancellationToken cancellationToken)
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
            return new ScorePoint(local, score.Total, score.Risk, score.Response, score.MinimumMonitoringFrequency,
                score.SubScores, score.ParametersScoring3, score.Problems, reading.Input, reading.Notes);
        }).ToList();
    }
}
