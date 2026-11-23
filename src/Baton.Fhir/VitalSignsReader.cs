using System.Globalization;
using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir;

/// <summary>
/// Reads NEWS2 vital signs from FHIR R4 Observations coded with LOINC. For each parameter the most
/// recent reading decides: if it cannot be used (no time, in the future, stale, not measured, no exact
/// value, unknown unit or answer) the parameter is left empty and the reason is noted. An older reading
/// is never used in place of a newer broken one, and room air is never assumed.
/// </summary>
public static class VitalSignsReader
{
    private static readonly HashSet<ObservationStatus> UsableStatuses =
        [ObservationStatus.Final, ObservationStatus.Amended, ObservationStatus.Corrected, ObservationStatus.Preliminary];

    private static readonly string[] PerMinuteUnits = ["/min", "{breaths}/min", "{beats}/min", "{counts}/min"];

    private sealed record Candidate(Observation Source, DataType? Value, CodeableConcept? Absent, DateTimeOffset? Time, string Code);

    public static VitalSignsReading Read(IEnumerable<Observation> observations, ReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(options);

        var usable = observations.Where(o => o?.Status is { } s && UsableStatuses.Contains(s)).ToList();
        var sources = new Dictionary<News2Parameter, Observation>();
        var notes = new List<string>();

        var input = new News2Input
        {
            RespirationRate = ReadInteger(News2Parameter.RespirationRate, Candidates(usable, Loinc.RespirationRate), PerMinuteUnits),
            SpO2 = ReadInteger(News2Parameter.SpO2, Candidates(usable, Loinc.SpO2), ["%"]),
            SpO2Scale = options.SpO2Scale,
            Scale2Authorisation = options.Scale2Authorisation,
            OnSupplementalOxygen = ReadOxygen(),
            SystolicBp = ReadInteger(News2Parameter.SystolicBp, SystolicCandidates(usable), ["mm[Hg]"]),
            PulseRate = ReadInteger(News2Parameter.PulseRate, Candidates(usable, Loinc.PulseRate), PerMinuteUnits),
            Consciousness = ReadConsciousness(),
            TemperatureCelsius = ReadTemperature(),
        };

        return new VitalSignsReading(input, sources, notes);

        int? ReadInteger(News2Parameter parameter, List<Candidate> candidates, string[] units)
        {
            if (Quantity(parameter, candidates, units) is not var (candidate, value))
            {
                return null;
            }

            var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (rounded is > int.MaxValue or < int.MinValue)
            {
                Note(parameter, $"value {value} is out of range");
                return null;
            }

            if (rounded != value)
            {
                Note(parameter, $"{value} rounded to {rounded}");
            }

            sources[parameter] = candidate.Source;
            return (int)rounded;
        }

        decimal? ReadTemperature()
        {
            if (Quantity(News2Parameter.Temperature, Candidates(usable, Loinc.Temperature), ["Cel", "[degF]"]) is not var (candidate, value))
            {
                return null;
            }

            sources[News2Parameter.Temperature] = candidate.Source;
            if (((Quantity)candidate.Value!).Code != "[degF]")
            {
                return value;
            }

            var celsius = (value - 32m) * 5m / 9m;
            Note(News2Parameter.Temperature, $"converted from {value} °F to {celsius:0.###} °C");
            return celsius;
        }

        bool? ReadOxygen()
        {
            const News2Parameter parameter = News2Parameter.SupplementalOxygen;
            var candidates = Candidates(usable, [Loinc.OxygenFlow, Loinc.InspiredOxygen]);
            if (Usable(parameter, candidates) is not { } candidate)
            {
                if (candidates.Count == 0)
                {
                    Note(parameter, $"no air or oxygen record; room air is not assumed");
                }

                return null;
            }

            var isFlow = candidate.Code == Loinc.OxygenFlow;
            if (ExactQuantity(parameter, candidate, isFlow ? ["L/min"] : ["%"]) is not { } value)
            {
                return null;
            }

            if (isFlow ? value < 0 : value is < 21 or > 100)
            {
                if (isFlow)
                {
                    Note(parameter, $"oxygen flow {value} L/min is negative");
                }
                else
                {
                    Note(parameter, $"inspired oxygen {value}% is outside 21–100%");
                }

                return null;
            }

            sources[parameter] = candidate.Source;
            return isFlow ? value > 0 : value > 21;
        }

        Consciousness? ReadConsciousness()
        {
            const News2Parameter parameter = News2Parameter.Consciousness;
            if (Usable(parameter, Candidates(usable, Loinc.Consciousness)) is not { } candidate)
            {
                return null;
            }

            var answer = (candidate.Value as CodeableConcept)?.Coding
                .FirstOrDefault(c => c.System == Loinc.System && c.Code is not null && Loinc.ConsciousnessAnswers.ContainsKey(c.Code));
            if (answer is null)
            {
                Note(parameter, $"answer is not a recognised LOINC ACVPU code");
                return null;
            }

            sources[parameter] = candidate.Source;
            return Loinc.ConsciousnessAnswers[answer.Code!];
        }

        (Candidate Candidate, decimal Value)? Quantity(News2Parameter parameter, List<Candidate> candidates, string[] units)
        {
            if (Usable(parameter, candidates) is not { } candidate)
            {
                return null;
            }

            return ExactQuantity(parameter, candidate, units) is { } value ? (candidate, value) : null;
        }

        decimal? ExactQuantity(News2Parameter parameter, Candidate candidate, string[] units)
        {
            if (candidate.Value is not Quantity { Value: { } value } quantity)
            {
                Note(parameter, $"the latest reading has no value");
                return null;
            }

            if (quantity.Comparator is not null)
            {
                Note(parameter, $"the latest reading is not an exact value");
                return null;
            }

            if (quantity.System != Loinc.Ucum || quantity.Code is null || !units.Contains(quantity.Code))
            {
                Note(parameter, $"unit '{quantity.Code ?? quantity.Unit ?? "none"}' is not supported");
                return null;
            }

            return value;
        }

        // The most recent candidate decides; an older one never stands in for a newer broken one.
        Candidate? Usable(News2Parameter parameter, List<Candidate> candidates)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            // A reading without a full date and time cannot be ordered, so it might be the newest one.
            var timeless = candidates.Count(c => c.Time is null);
            if (timeless > 0)
            {
                Note(parameter, $"{timeless} reading(s) have no usable time (date and time with offset), so the latest reading cannot be determined");
                return null;
            }

            var newestUtc = candidates.Max(c => c.Time!.Value.UtcDateTime);
            var newest = candidates.Where(c => c.Time!.Value.UtcDateTime == newestUtc).ToList();
            if (newest.Select(ValueKey).Distinct().Count() > 1)
            {
                Note(parameter, $"conflicting readings at the same latest time ({newest.Count})");
                return null;
            }

            var latest = newest[0];
            var time = latest.Time!.Value;
            if (time > options.Now)
            {
                Note(parameter, $"the latest reading is in the future");
                return null;
            }

            if (options.Now - time > options.MaxAge)
            {
                Note(parameter, $"the latest reading is older than {options.MaxAge.TotalMinutes:0} minutes");
                return null;
            }

            if (latest.Absent is not null)
            {
                Note(parameter, $"the latest reading was not measured (data absent reason)");
                return null;
            }

            return latest;
        }

        // FormattableString so numbers inside the message are also written in the invariant culture.
        void Note(News2Parameter parameter, FormattableString message) =>
            notes.Add($"{parameter}: {message.ToString(CultureInfo.InvariantCulture)}.");
    }

    // Identifies what a reading says, to tell agreeing duplicates from conflicting readings.
    private static string ValueKey(Candidate candidate) => candidate switch
    {
        { Absent: not null } => "absent",
        { Value: Quantity q } => string.Create(CultureInfo.InvariantCulture, $"{q.Value}|{q.Comparator}|{q.System}|{q.Code}"),
        { Value: CodeableConcept c } => string.Join(",", c.Coding.Select(x => $"{x.System}|{x.Code}").Order()),
        _ => candidate.Value?.GetType().Name ?? "none",
    };

    private static List<Candidate> Candidates(List<Observation> observations, string[] codes) =>
        observations
            .Select(o => (Observation: o, Code: MatchingCode(o.Code, codes)))
            .Where(x => x.Code is not null)
            .Select(x => new Candidate(x.Observation, x.Observation.Value, x.Observation.DataAbsentReason, TimeOf(x.Observation), x.Code!))
            .ToList();

    private static List<Candidate> SystolicCandidates(List<Observation> observations)
    {
        var candidates = Candidates(observations, [Loinc.Systolic]);
        foreach (var panel in observations.Where(o => MatchingCode(o.Code, [Loinc.BloodPressurePanel]) is not null))
        {
            var component = panel.Component.FirstOrDefault(c => MatchingCode(c.Code, [Loinc.Systolic]) is not null);
            if (component is not null)
            {
                candidates.Add(new Candidate(panel, component.Value, component.DataAbsentReason ?? panel.DataAbsentReason, TimeOf(panel), Loinc.Systolic));
            }
        }

        return candidates;
    }

    private static string? MatchingCode(CodeableConcept? concept, string[] codes) =>
        concept?.Coding.FirstOrDefault(c => c.System == Loinc.System && c.Code is not null && codes.Contains(c.Code))?.Code;

    private static DateTimeOffset? TimeOf(Observation observation) => observation.Effective switch
    {
        FhirDateTime dt when dt.TryToDateTimeOffset(out var value) => value,
        Instant { Value: { } value } => value,
        Period { End: { } end } when new FhirDateTime(end).TryToDateTimeOffset(out var value) => value,
        _ => null,
    };
}
