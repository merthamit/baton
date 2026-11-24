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
            new(SbarPart.Recommendation, Recommendation(context)),
        };

        return new Sbar(
            input.At,
            context.WindowStart,
            text.Language,
            sections,
            Missing(context),
            StateLine(context),
            context.Latest?.Total,
            context.Latest?.Risk,
            context.Peak?.Total,
            FloorRisk(context.Latest));
    }

    /// <summary>The risk a score already shows: its own, or for an incomplete set the least its known parts imply.</summary>
    private static RiskLevel? FloorRisk(ScorePoint? point) => point switch
    {
        null => null,
        { Risk: { } risk } => risk,
        { MinimumTotal: >= 7 } => RiskLevel.High,
        { MinimumTotal: >= 5 } => RiskLevel.Medium,
        { ParametersScoring3.Count: > 0 } => RiskLevel.LowMedium,
        _ => RiskLevel.Low,
    };

    /// <summary>Asks for escalation: risk Low–medium or higher, or an incomplete set whose known parts already do.</summary>
    private static bool Raised(ScorePoint point) => FloorRisk(point) is RiskLevel.LowMedium or RiskLevel.Medium or RiskLevel.High;

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
            State = EscalationChain.At(
                Records,
                input.Points.Where(p => p.At <= input.At).Select(p => new ScoreMark(p.At, p.Risk)).ToList(),
                input.At,
                input.Options);
            var inWindow = CallsInWindow().Select(call => call.CallId).ToHashSet(StringComparer.Ordinal);
            WindowCalls = State.Calls.Where(call => inWindow.Contains(call.CallId)).ToList();
        }

        /// <summary>The chain at the moment (all records up to it), as the board computes it.</summary>
        public EscalationState State { get; }

        /// <summary>The chain's calls that belong to this handover, in call order.</summary>
        public IReadOnlyList<CallState> WindowCalls { get; }

        public SourceRef Of(CallState call) => new($"Task/{call.CallId}", call.CalledAt, Text.CallLabel(call.Tier, Time(call.CalledAt)));

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
        public IReadOnlyList<Called> CallsInWindow()
        {
            // The episode goes on: when a score in the window still asks for escalation, the last call before the
            // window belongs to this handover too (the team already involved), so the reader sees who came.
            // Only while the scores stayed raised: a Low score between that call and the window's first raised score
            // ends the episode, and a new deterioration is new.
            var firstRaised = Points.FirstOrDefault(Raised);
            var before = Records.OfType<Called>().LastOrDefault(call => call.At < WindowStart);
            var episode = firstRaised is not null && before is not null && !Interrupted(before.At, firstRaised.At) ? before : null;
            return Records.OfType<Called>()
                .Where(call =>
                {
                    var steps = Records.Where(r => CallIdOf(r) == call.CallId).ToList();
                    var arrival = steps.OfType<Arrived>().FirstOrDefault();
                    return call.At >= WindowStart
                        || steps.Any(s => s.At >= WindowStart)
                        || arrival is null || arrival.At > WindowStart
                        || call == episode;
                })
                .ToList();
        }

        /// <summary>True when a score between the two moments did not ask for escalation (the episode settled).</summary>
        public bool Interrupted(DateTimeOffset from, DateTimeOffset to) =>
            Input.Points.Any(q => q.At > from && q.At < to && !Raised(q));

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

    private static List<SbarSentence> Recommendation(Context c)
    {
        var t = c.Text;
        var sentences = new List<SbarSentence>();
        if (c.Latest is not null && c.Input.NextDue is { } due)
        {
            sentences.Add(Say(t.NextDue(c.Time(due), c.Input.OverdueMinutes), c.Of(c.Latest)));
        }

        sentences.Add(Contingency(c));

        if (c.State.OpenCall is { } open)
        {
            sentences.Add(Say(t.OpenCall(open.Tier, c.Time(open.CalledAt)), [c.Of(open)]));
        }

        foreach (var record in c.Records.Where(r => r.At >= c.WindowStart))
        {
            switch (record)
            {
                case ConcernRaised concern:
                    sentences.Add(Say(t.OpenConcern(c.Time(concern.At)), [c.Of(concern)]));
                    break;
                case PatientOrFamilyConcern voice:
                    sentences.Add(Say(t.OpenFamilyConcern(c.Time(voice.At)), [c.Of(voice)]));
                    break;
            }
        }

        return sentences;
    }

    /// <summary>
    /// Exactly one "if this, then that" line from existing rules only; the first match wins (spec "Precedence").
    /// Critical care comes first, as in the chain's own suggestion.
    /// </summary>
    private static SbarSentence Contingency(Context c)
    {
        var t = c.Text;
        var state = c.State;
        var calls = state.Calls;
        var risk = c.Latest?.Risk;
        var latest = c.LatestOrPatient();

        if (c.Latest is null)
        {
            return Say(t.TakeObservations, c.PatientSources());
        }

        if (c.Latest.Total is null)
        {
            var parts = c.Latest.Problems.Select(p => p.Parameter).Distinct().Select(t.Parameter).ToList();
            return Say(t.CompleteSet(parts.Count > 0 ? string.Join(", ", parts) : null), latest);
        }

        if (risk == RiskLevel.High && calls.All(call => call.Tier != EscalationTier.CriticalCare))
        {
            return Say(t.CallCriticalCareNow, latest);
        }

        if (state.TargetPassed && state.SuggestedNextTier is { } suggested && state.OpenCall is { TargetAt: { } passedAt } passed)
        {
            return Say(t.TargetPassed(c.Time(passedAt), suggested), [c.Of(passed)]);
        }

        if (state.OpenCall is { } open)
        {
            var target = open.TargetAt is { } at ? c.Time(at) : null;
            return open.Tier == EscalationTier.CriticalCare
                ? Say(t.ChaseCriticalCare(target), [c.Of(open)])
                : Say(t.IfNoReview(target, open.Tier + 1), [c.Of(open)]);
        }

        if (risk == RiskLevel.High && calls.Any(call => call.Tier == EscalationTier.CriticalCare && call.ArrivedAt is not null))
        {
            return Say(t.TellCriticalCareIfWorse, latest);
        }

        return risk is RiskLevel.Medium or RiskLevel.LowMedium
            ? Say(t.IfReachesSeven(c.Input.Options.HighTargetMinutes), latest)
            : Say(t.IfReachesFive(c.Input.Options.MediumTargetMinutes), latest);
    }

    private static List<MissingItem> Missing(Context c)
    {
        var t = c.Text;
        var missing = new List<MissingItem>();
        var calls = c.Records.OfType<Called>().ToList();

        if (c.Latest is null)
        {
            missing.Add(new(MissingKind.NoObservations, t.MissingNoObservations, c.PatientSources()));
        }

        // The first measurement asking for escalation with no call at or after it and no call of this handover's
        // episode before it (a call already made in the window, answered or still open, covers the scores after it;
        // a 7+ still needs critical care, below).
        var uncovered = c.Points.FirstOrDefault(p =>
            Raised(p)
            && !calls.Any(call => call.At >= p.At)
            && !c.WindowCalls.Any(call => call.CalledAt <= p.At && !c.Interrupted(call.CalledAt, p.At)));
        if (uncovered is not null)
        {
            missing.Add(new(MissingKind.NoCallAfterScore, t.MissingNoCall(uncovered.Total ?? uncovered.MinimumTotal ?? 0, c.Time(uncovered.At)), c.Of(uncovered)));
        }

        // The first 7+ with no critical care call (the chain suggests critical care for exactly this case).
        var high = c.Points.FirstOrDefault(p => p.Risk == RiskLevel.High);
        if (high is not null && calls.All(call => call.Tier != EscalationTier.CriticalCare))
        {
            missing.Add(new(MissingKind.NoCriticalCareCall, t.MissingNoCriticalCare(high.Total ?? high.MinimumTotal ?? 0, c.Time(high.At)), c.Of(high)));
        }

        foreach (var call in c.WindowCalls)
        {
            if (call.ArrivedAt is null)
            {
                if (call.TargetAt is { } target && target < c.Input.At)
                {
                    missing.Add(new(MissingKind.NoArrival, t.MissingArrival(call.Tier), [c.Of(call)]));
                }
            }
            else if (call.AssessedAt is null)
            {
                missing.Add(new(MissingKind.NoAssessment, t.MissingAssessment(call.Tier), [c.Of(call)]));
            }
            else if (call.PlannedAt is null)
            {
                missing.Add(new(MissingKind.NoPlan, t.MissingPlan(call.Tier), [c.Of(call)]));
            }
        }

        if (c.Latest is not null && c.Input.OverdueMinutes is { } overdue && overdue > 0)
        {
            missing.Add(new(MissingKind.ObservationOverdue, t.MissingOverdue(overdue), c.Of(c.Latest)));
        }

        return missing;
    }

    /// <summary>One line for the handover list: how far the latest call has got, or whether the patient is stable.</summary>
    private static string StateLine(Context c)
    {
        var t = c.Text;
        if (c.WindowCalls.Count > 0)
        {
            var call = c.WindowCalls.OrderBy(x => x.CalledAt).Last();
            return call switch
            {
                { PlannedAt: { } planned } => t.StatePlanned(call.Tier, c.Time(planned)),
                { AssessedAt: { } assessed } => t.StateAssessed(call.Tier, c.Time(assessed)),
                { ArrivedAt: { } arrived } => t.StateArrived(call.Tier, c.Time(arrived)),
                _ => t.StateWaiting(call.Tier, c.Time(call.CalledAt), (int)Math.Floor((c.Input.At - call.CalledAt).TotalMinutes)),
            };
        }

        if (c.Latest is null)
        {
            return t.StateNoObservations;
        }

        if (c.Latest.Total is null)
        {
            return t.StateIncomplete;
        }

        return c.Points.Any(Raised) ? t.StateNotEscalated : t.StateStable;
    }
}
