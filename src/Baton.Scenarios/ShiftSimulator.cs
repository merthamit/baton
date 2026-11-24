using Baton.Core.News2;

namespace Baton.Scenarios;

/// <summary>How to run one patient's shift.</summary>
/// <param name="ShiftStart">08:00 of the shift; hour 0.</param>
public sealed record SimulationOptions(DateTimeOffset ShiftStart)
{
    /// <summary>Seed for measurement noise. The same seed always gives the same shift.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>0 = no noise (the card exactly); 1 = normal measurement noise.</summary>
    public double NoiseLevel { get; init; }

    /// <summary>How many hours later than the card's timely treatment the treatment starts (decision 2B).</summary>
    public double TreatmentDelayHours { get; init; }
}

public sealed record Measurement(double Hour, DateTimeOffset At, VitalSet Vitals, News2Input Input, News2Result Score);

public sealed record SimulatedPatient(ScenarioCard Card, IReadOnlyList<Measurement> Measurements, IReadOnlyList<ScenarioEvent> Events);

/// <summary>
/// Plays a scenario card through one 24-hour shift. The nurse measures on the RCP NEWS2 schedule
/// (0 → 12 h, 1–4 → 4 h, a single 3 or 5+ → hourly); a concern flag brings the next measurement within
/// 1 hour and then at least every 2 hours (decision 3C). Alerts, doctor calls and critical care calls
/// are derived from the scores.
/// </summary>
public static class ShiftSimulator
{
    public const double ShiftHours = 24;
    private const double DoctorCallDelayHours = 4 / 60.0;
    private const double CriticalCareCallDelayHours = 5 / 60.0;

    public static SimulatedPatient Run(ScenarioCard card, SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.TreatmentDelayHours);
        ArgumentOutOfRangeException.ThrowIfNegative(options.NoiseLevel);

        var concerns = card.Events.Where(e => e.Kind == ScenarioEventKind.Concern).Select(e => e.Hour).Order().ToList();
        var scale2 = card.Events.FirstOrDefault(e => e.Kind == ScenarioEventKind.Scale2Decision);
        var random = new Random(options.Seed);

        var measurements = new List<Measurement>();
        var events = card.Events
            .Select(e => Shifted(e, card, options))
            .Where(e => e.Hour < ShiftHours)
            .ToList();
        var alerted = false;
        var criticalCareCalled = false;

        var hour = 0.0;
        while (hour < ShiftHours)
        {
            var authorisation = scale2 is not null && scale2.Hour <= hour
                ? new Scale2Authorisation(scale2.Text, AtMinute(options.ShiftStart, scale2.Hour))
                : null;
            var (vitals, input, score) = Measure(Delay.Apply(card, hour, options.TreatmentDelayHours), card, authorisation, random, options.NoiseLevel);
            measurements.Add(new Measurement(hour, AtMinute(options.ShiftStart, hour), vitals, input, score));

            var urgent = score.Risk is RiskLevel.LowMedium or RiskLevel.Medium or RiskLevel.High;
            if (urgent && !alerted)
            {
                alerted = true;
                events.Add(new ScenarioEvent(hour, ScenarioEventKind.Alert, $"NEWS2 {score.Total}: urgent review needed"));
                events.Add(new ScenarioEvent(hour + DoctorCallDelayHours, ScenarioEventKind.DoctorCalled, "Nurse called the on-call doctor"));
            }

            if (score.Risk == RiskLevel.High && !criticalCareCalled)
            {
                criticalCareCalled = true;
                events.Add(new ScenarioEvent(hour + CriticalCareCallDelayHours, ScenarioEventKind.CriticalCareCalled, $"NEWS2 {score.Total}: critical care team called"));
            }

            hour = NextMeasurement(options.ShiftStart, hour, score, concerns);
        }

        return new SimulatedPatient(card, measurements, events.OrderBy(e => e.Hour).ToList());
    }

    /// <summary>Clock time of an hour of the shift, rounded to the whole minute.</summary>
    public static DateTimeOffset AtMinute(DateTimeOffset shiftStart, double hour) =>
        shiftStart.AddMinutes(Math.Round(hour * 60, MidpointRounding.AwayFromZero));

    // Noise is cosmetic: a noisy reading is only used if it gives the same NEWS2 total and risk as the
    // card, so the story (alerts, schedule, doctor and treatment) never changes with the seed.
    private const int NoiseAttempts = 10;

    private static (VitalSet Vitals, News2Input Input, News2Result Score) Measure(
        VitalSet truth, ScenarioCard card, Scale2Authorisation? authorisation, Random random, double noiseLevel)
    {
        var input = truth.ToNews2Input(card.Scale, authorisation);
        var score = News2Calculator.Calculate(input);
        for (var attempt = 0; noiseLevel > 0 && attempt < NoiseAttempts; attempt++)
        {
            var noisy = MeasurementNoise.Apply(truth, random, noiseLevel);
            var noisyInput = noisy.ToNews2Input(card.Scale, authorisation);
            var noisyScore = News2Calculator.Calculate(noisyInput);
            if (noisyScore.Total == score.Total && noisyScore.Risk == score.Risk)
            {
                return (noisy, noisyInput, noisyScore);
            }
        }

        return (truth, input, score);
    }

    private static double NextMeasurement(DateTimeOffset shiftStart, double hour, News2Result score, List<double> concerns)
    {
        var due = MonitoringSchedule.NextDue(shiftStart.AddHours(hour), score.Response, concerns.Select(c => shiftStart.AddHours(c)).ToList());
        return (due - shiftStart).TotalHours;
    }

    // Doctor arrival, treatment and anything the card says happens after the treatment move with the delay,
    // like the vitals do. Concerns and the Scale 2 decision stay where the card put them.
    private static ScenarioEvent Shifted(ScenarioEvent e, ScenarioCard card, SimulationOptions options)
    {
        if (card.Delay is null || options.TreatmentDelayHours == 0 || e.Kind is ScenarioEventKind.Concern or ScenarioEventKind.Scale2Decision)
        {
            return e;
        }

        var follows = e.Kind is ScenarioEventKind.DoctorArrived or ScenarioEventKind.TreatmentStarted
            || e.Hour >= card.Delay.TreatmentHour;
        return follows ? e with { Hour = e.Hour + options.TreatmentDelayHours } : e;
    }
}
