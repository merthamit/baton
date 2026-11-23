using Baton.Core.News2;
using Baton.Fhir;
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

public sealed record BedSummary(string PatientId, string Name, string? Bed, ScorePoint? Latest, ScorePoint? Previous);

public sealed record PatientSeries(PatientRef Patient, IReadOnlyList<ScorePoint> Points);

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
            result.Add(new BedSummary(
                patient.Id,
                patient.Name,
                patient.Bed,
                points.Count > 0 ? points[^1] : null,
                points.Count > 1 ? points[^2] : null));
        }

        return result;
    }

    public async Task<PatientSeries?> GetPatientAsync(string patientId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var patient = (await source.GetPatientsAsync(cancellationToken)).FirstOrDefault(p => p.Id == patientId);
        return patient is null ? null : new PatientSeries(patient, await ScoreAsync(patient, at, cancellationToken));
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
