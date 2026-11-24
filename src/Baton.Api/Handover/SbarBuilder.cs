using System.Globalization;
using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Fhir;

namespace Baton.Api.Handover;

/// <summary>
/// Builds a patient's SBAR handover from the record, by rules only (decision 16B). Pure: no clock, no storage.
/// Every sentence carries its sources; what the record does not hold becomes a missing item, never invented text.
/// </summary>
public static class SbarBuilder
{
    /// <summary>The handover covers the 12 hours up to the moment (a nursing shift), clipped to the scenario start.</summary>
    public const int WindowHours = 12;

    public static Sbar Build(SbarInput input, string? language)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = SbarText.For(language);
        var context = new Context(input, text);

        var sections = new List<SbarSection>
        {
            new(SbarPart.Situation, Situation(context)),
            new(SbarPart.Background, Background(context)),
            new(SbarPart.Assessment, Assessment(context)),
            new(SbarPart.Recommendation, []),
        };

        return new Sbar(
            input.At,
            context.WindowStart,
            text.Language,
            sections,
            [],
            "",
            context.Latest?.Total,
            context.Latest?.Risk,
            context.Peak?.Total);
    }

    /// <summary>What every part reads: the window, its points and records, and how to render times.</summary>
    private sealed class Context
    {
        public Context(SbarInput input, SbarText text)
        {
            Input = input;
            Text = text;
            var windowStart = input.At.AddHours(-WindowHours);
            WindowStart = (windowStart < input.ShiftStart ? input.ShiftStart : windowStart).ToOffset(input.At.Offset);
            Points = input.Points.Where(p => p.At >= WindowStart && p.At <= input.At).OrderBy(p => p.At).ToList();
            Records = input.Records.Where(r => r.At <= input.At).OrderBy(r => r.At).ToList();
            Latest = Points.Count > 0 ? Points[^1] : null;
            Peak = Points.Where(p => p.Total is not null).OrderByDescending(p => p.Total).ThenBy(p => p.At).FirstOrDefault();
        }

        public SbarInput Input { get; }

        public SbarText Text { get; }

        public DateTimeOffset WindowStart { get; }

        public IReadOnlyList<ScorePoint> Points { get; }

        public IReadOnlyList<EscalationRecord> Records { get; }

        public ScorePoint? Latest { get; }

        public ScorePoint? Peak { get; }

        public string PatientId => Input.Patient.Id;

        public string Time(DateTimeOffset at) => at.ToOffset(Input.At.Offset).ToString("HH:mm", CultureInfo.InvariantCulture);

        public IReadOnlyList<SourceRef> Of(ScorePoint point) =>
            point.Sources.Select(reference => new SourceRef(reference, point.At, Text.ObservationsLabel(Time(point.At)))).ToList();

        public SourceRef Of(EscalationRecord record) => new(
            EscalationFhir.ReferenceOf(record, PatientId),
            record.At,
            record switch
            {
                ConcernRaised => Text.ConcernLabel(Time(record.At)),
                PatientOrFamilyConcern => Text.FamilyLabel(Time(record.At)),
                _ => CallOf(record) is { } call ? Text.CallLabel(call.Tier, Time(call.At)) : Text.PatientLabel,
            });

        public IReadOnlyList<SourceRef> PatientSources()
        {
            var sources = new List<SourceRef> { new($"Patient/{PatientId}", Input.At, Text.PatientLabel) };
            if (Input.Patient.Diagnosis is not null)
            {
                sources.Add(new($"Condition/condition-{PatientId}", Input.At, Text.DiagnosisLabel));
            }

            return sources;
        }

        /// <summary>The latest measurement's sources, or the patient record when there is none.</summary>
        public IReadOnlyList<SourceRef> LatestOrPatient() => Latest is null ? PatientSources() : Of(Latest);

        public Called? CallOf(EscalationRecord record) => record switch
        {
            Called c => c,
            Arrived a => Records.OfType<Called>().FirstOrDefault(c => c.CallId == a.CallId),
            Assessed a => Records.OfType<Called>().FirstOrDefault(c => c.CallId == a.CallId),
            Planned p => Records.OfType<Called>().FirstOrDefault(c => c.CallId == p.CallId),
            _ => null,
        };

        /// <summary>
        /// Calls that belong to this handover: made in the window, or with a step in the window, or still open (no
        /// arrival) at the window's start.
        /// </summary>
        public IReadOnlyList<Called> CallsInWindow() => Records.OfType<Called>()
            .Where(call =>
            {
                var steps = Records.Where(r => CallIdOf(r) == call.CallId).ToList();
                var arrival = steps.OfType<Arrived>().FirstOrDefault();
                return call.At >= WindowStart
                    || steps.Any(s => s.At >= WindowStart)
                    || arrival is null || arrival.At > WindowStart;
            })
            .ToList();

        public static string? CallIdOf(EscalationRecord record) => record switch
        {
            Called c => c.CallId,
            Arrived a => a.CallId,
            Assessed a => a.CallId,
            Planned p => p.CallId,
            _ => null,
        };
    }

    private static SbarSentence Say(string text, IReadOnlyList<SourceRef> sources) => new(Sentence(text), sources);

    /// <summary>Ends the text with a full stop unless it already ends a sentence.</summary>
    private static string Sentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?') ? trimmed : trimmed + ".";
    }

    private static List<SbarSentence> Situation(Context c)
    {
        var t = c.Text;
        var sentences = new List<SbarSentence>();
        if (c.Latest is not { } latest)
        {
            sentences.Add(Say(t.NoObservations, c.PatientSources()));
            return sentences;
        }

        var time = c.Time(latest.At);
        sentences.Add(Say(latest.Total is { } total ? t.Score(total, latest.Risk, time) : t.NotScored(time), c.Of(latest)));

        var incomplete = latest.Problems.Select(p => p.Parameter).Distinct().ToList();
        if (incomplete.Count > 0)
        {
            sentences.Add(Say(t.Incomplete(time, string.Join(", ", incomplete.Select(t.Parameter))), c.Of(latest)));
        }

        if (c.Peak is { } peak && peak.Total > (latest.Total ?? -1))
        {
            sentences.Add(Say(t.Peak(peak.Total!.Value, peak.Risk, c.Time(peak.At)), c.Of(peak)));
        }

        var drivers = WardService.DriversOf(latest.SubScores);
        if (drivers.Count > 0)
        {
            sentences.Add(Say(t.Drivers(string.Join(", ", drivers.Select(d => Reading(t, d, latest.Input)))), c.Of(latest)));
        }

        return sentences;
    }

    /// <summary>"Resp 21", "SpO₂ 92", "Oxygen", "ACVPU C", "Temp 38.3": the parameter and the value that scored.</summary>
    private static string Reading(SbarText t, News2Parameter parameter, News2Input input)
    {
        var name = t.Parameter(parameter);
        string? value = parameter switch
        {
            News2Parameter.RespirationRate => input.RespirationRate?.ToString(CultureInfo.InvariantCulture),
            News2Parameter.SpO2 => input.SpO2?.ToString(CultureInfo.InvariantCulture),
            News2Parameter.SystolicBp => input.SystolicBp?.ToString(CultureInfo.InvariantCulture),
            News2Parameter.PulseRate => input.PulseRate?.ToString(CultureInfo.InvariantCulture),
            News2Parameter.Temperature => input.TemperatureCelsius?.ToString("0.0", t.Culture),
            News2Parameter.Consciousness => input.Consciousness?.ToString()[..1],
            _ => null,
        };
        return value is null ? name : $"{name} {value}";
    }

    private static List<SbarSentence> Background(Context c)
    {
        var t = c.Text;
        var sentences = new List<SbarSentence> { Say(t.AgeAndDiagnosis(c.Input.Age, c.Input.Patient.Diagnosis), c.PatientSources()) };
        foreach (var record in c.Records.Where(r => r.At >= c.WindowStart))
        {
            switch (record)
            {
                case ConcernRaised concern:
                    sentences.Add(Say(t.NurseConcern(c.Time(concern.At), concern.Note), [c.Of(concern)]));
                    break;
                case PatientOrFamilyConcern voice:
                    sentences.Add(Say(t.FamilyConcern(c.Time(voice.At), voice.Text), [c.Of(voice)]));
                    break;
            }
        }

        return sentences;
    }

    private static List<SbarSentence> Assessment(Context c)
    {
        var t = c.Text;
        var sentences = new List<SbarSentence>();
        foreach (var call in c.CallsInWindow())
        {
            sentences.Add(Say(t.Called(call.Tier, c.Time(call.At)), [c.Of(call)]));
            foreach (var step in c.Records.Where(r => Context.CallIdOf(r) == call.CallId && r is not Called))
            {
                sentences.Add(step switch
                {
                    Arrived a => Say(t.Arrived(c.Time(a.At), a.Simulated), [c.Of(a)]),
                    Assessed a => Say(t.Assessment(a.Text), [c.Of(a)]),
                    Planned p => Say(t.Plan(p.Text), [c.Of(p)]),
                    _ => throw new InvalidOperationException($"Unexpected call step {step.GetType().Name}."),
                });
            }
        }

        if (sentences.Count == 0)
        {
            sentences.Add(Say(t.NoEscalation, c.LatestOrPatient()));
        }

        return sentences;
    }
}
