using System.Globalization;

namespace Baton.Core.News2;

/// <summary>
/// Calculates the National Early Warning Score 2 exactly as published by the Royal College of
/// Physicians (2017). Pure: no clock, randomness or I/O. Not a medical device.
/// </summary>
/// <remarks>
/// Clinical data problems (missing, implausible, Scale 2 without a recorded decision) never throw and
/// are never scored as zero: the parameter is left unscored and reported in
/// <see cref="News2Result.Problems"/>. Programming errors (null input, undefined enum values) throw.
/// </remarks>
public static class News2Calculator
{
    public const string AlgorithmVersion = "RCP-NEWS2-2017";

    // Outer limits of a value that could be a real observation. Not clinical ranges.
    private const int MinRespirationRate = 1, MaxRespirationRate = 80;
    private const int MinSpO2 = 1, MaxSpO2 = 100;
    private const int MinSystolicBp = 1, MaxSystolicBp = 300;
    private const int MinPulseRate = 1, MaxPulseRate = 300;
    private const decimal MinTemperature = 25.0m, MaxTemperature = 45.0m;

    public static News2Result Calculate(News2Input input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureDefined(input.SpO2Scale, nameof(input.SpO2Scale));
        EnsureDefined(input.ConfusionOnset, nameof(input.ConfusionOnset));
        if (input.Consciousness is { } level)
        {
            EnsureDefined(level, nameof(input.Consciousness));
        }

        var subScores = Enum.GetValues<News2Parameter>().ToDictionary(p => p, _ => (int?)null);
        var problems = new List<News2Problem>();
        var notes = new List<string>();

        // Set when SpO2 cannot be scored but scores 3 on every scale it could be on.
        var spo2IsRedWhateverTheScale = false;

        subScores[News2Parameter.RespirationRate] = ScoreInteger(
            input.RespirationRate, News2Parameter.RespirationRate, MinRespirationRate, MaxRespirationRate, News2Bands.Respiration);

        if (input.OnSupplementalOxygen is { } onOxygen)
        {
            subScores[News2Parameter.SupplementalOxygen] = News2Bands.SupplementalOxygen(onOxygen);
        }
        else
        {
            var spo2Blocked = input.SpO2Scale == SpO2Scale.Scale2 && input.SpO2 is > 92 and <= MaxSpO2
                && !string.IsNullOrWhiteSpace(input.Scale2Authorisation?.AuthorisedBy);
            AddProblem(News2Parameter.SupplementalOxygen, ProblemCode.Missing,
                "Air or oxygen was not recorded; room air is not assumed."
                + (spo2Blocked ? " SpO2 ≥93% on Scale 2 cannot be scored until air or oxygen is recorded." : string.Empty));
        }

        subScores[News2Parameter.SpO2] = ScoreSpO2();

        subScores[News2Parameter.SystolicBp] = ScoreInteger(
            input.SystolicBp, News2Parameter.SystolicBp, MinSystolicBp, MaxSystolicBp, News2Bands.Systolic);

        subScores[News2Parameter.PulseRate] = ScoreInteger(
            input.PulseRate, News2Parameter.PulseRate, MinPulseRate, MaxPulseRate, News2Bands.Pulse);

        if (input.Consciousness is { } consciousness)
        {
            subScores[News2Parameter.Consciousness] = News2Bands.Consciousness(consciousness, input.ConfusionOnset);
        }
        else
        {
            AddProblem(News2Parameter.Consciousness, ProblemCode.Missing, "Consciousness was not recorded.");
        }

        subScores[News2Parameter.Temperature] = ScoreTemperature();

        // Reported even when incomplete: a single red parameter must never be hidden by a missing one.
        var parametersScoring3 = subScores
            .Where(kv => kv.Value == 3 || (kv.Key == News2Parameter.SpO2 && spo2IsRedWhateverTheScale))
            .Select(kv => kv.Key)
            .ToList();
        var isComplete = problems.Count == 0;

        var result = new News2Result
        {
            IsComplete = isComplete,
            MinimumTotal = isComplete ? null : subScores.Values.Sum(v => v ?? 0),
            SubScores = subScores,
            HasSingleParameterScoreOf3 = parametersScoring3.Count > 0,
            ParametersScoring3 = parametersScoring3,
            Problems = problems,
            Notes = notes,
            AlgorithmVersion = AlgorithmVersion,
        };

        if (!isComplete)
        {
            return result;
        }

        var total = subScores.Values.Sum(v => v!.Value);
        var (risk, response, frequency) = Classify(total, parametersScoring3.Count > 0);
        return result with { Total = total, Risk = risk, Response = response, MinimumMonitoringFrequency = frequency };

        int? ScoreInteger(int? value, News2Parameter parameter, int min, int max, Func<int, int> band)
        {
            if (value is not { } v)
            {
                AddProblem(parameter, ProblemCode.Missing, $"{parameter} was not recorded.");
                return null;
            }

            if (v < min || v > max)
            {
                AddProblem(parameter, ProblemCode.ImplausibleValue, $"{parameter} {v} is outside {min}–{max}.");
                return null;
            }

            return band(v);
        }

        int? ScoreSpO2()
        {
            if (input.SpO2 is not { } spo2)
            {
                AddProblem(News2Parameter.SpO2, ProblemCode.Missing, "SpO2 was not recorded.");
                return null;
            }

            if (spo2 < MinSpO2 || spo2 > MaxSpO2)
            {
                AddProblem(News2Parameter.SpO2, ProblemCode.ImplausibleValue, $"SpO2 {spo2}% is outside {MinSpO2}–{MaxSpO2}%.");
                return null;
            }

            if (input.SpO2Scale == SpO2Scale.Scale1)
            {
                return News2Bands.SpO2Scale1(spo2);
            }

            // RCP NEWS2 2017: Scale 2 only with a decision recorded by a competent clinical decision maker.
            if (string.IsNullOrWhiteSpace(input.Scale2Authorisation?.AuthorisedBy))
            {
                AddProblem(News2Parameter.SpO2, ProblemCode.Scale2NotAuthorised,
                    "SpO2 Scale 2 needs a recorded clinical decision; falling back to Scale 1 is not allowed.");

                // ≤83% scores 3 on Scale 1 and on Scale 2, so it must still be flagged red.
                spo2IsRedWhateverTheScale = spo2 <= 83;
                return null;
            }

            // On Scale 2, values of 93% and above score differently on air and on oxygen.
            if (input.OnSupplementalOxygen is { } onOxygen)
            {
                return News2Bands.SpO2Scale2(spo2, onOxygen);
            }

            return spo2 <= 92 ? News2Bands.SpO2Scale2(spo2, onSupplementalOxygen: false) : null;
        }

        int? ScoreTemperature()
        {
            if (input.TemperatureCelsius is not { } raw)
            {
                AddProblem(News2Parameter.Temperature, ProblemCode.Missing, "Temperature was not recorded.");
                return null;
            }

            var rounded = Math.Round(raw, 1, MidpointRounding.AwayFromZero);
            if (rounded != raw)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"Temperature rounded from {raw} to {rounded:0.0} °C"));
            }

            if (rounded < MinTemperature || rounded > MaxTemperature)
            {
                AddProblem(News2Parameter.Temperature, ProblemCode.ImplausibleValue,
                    string.Create(CultureInfo.InvariantCulture, $"Temperature {rounded:0.0} °C is outside {MinTemperature:0.0}–{MaxTemperature:0.0} °C."));
                return null;
            }

            return News2Bands.Temperature(rounded);
        }

        void AddProblem(News2Parameter parameter, ProblemCode code, string message) =>
            problems.Add(new News2Problem(parameter, code, message));
    }

    // RCP NEWS2 2017, Chart 2 (thresholds and triggers) and Chart 4 (clinical response).
    private static (RiskLevel Risk, ClinicalResponse Response, string Frequency) Classify(int total, bool anyParameterScoring3) =>
        (total, anyParameterScoring3) switch
        {
            ( >= 7, _) => (RiskLevel.High, ClinicalResponse.EmergencyResponse, "Continuous monitoring"),
            ( >= 5, _) => (RiskLevel.Medium, ClinicalResponse.UrgentResponse, "At least hourly"),
            (_, true) => (RiskLevel.LowMedium, ClinicalResponse.UrgentWardBasedResponse, "At least hourly"),
            ( >= 1, _) => (RiskLevel.Low, ClinicalResponse.WardBasedResponse, "At least 4–6-hourly"),
            _ => (RiskLevel.Low, ClinicalResponse.RoutineMonitoring, "At least 12-hourly"),
        };

    private static void EnsureDefined<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, value, $"Undefined {typeof(TEnum).Name} value.");
        }
    }
}
