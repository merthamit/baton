using Baton.Core.News2;

namespace Baton.Core.Escalation;

/// <summary>
/// Works out where a patient's escalation chain stands at a moment. Pure: no clock, no storage. Only records at or
/// before the moment count, in time order whatever order they arrive in.
/// </summary>
public static class EscalationChain
{
    /// <summary>The response target for a risk level, or null when no target applies (Low or unknown).</summary>
    public static TimeSpan? TargetFor(RiskLevel? risk, EscalationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return risk switch
        {
            RiskLevel.High => TimeSpan.FromMinutes(options.HighTargetMinutes),
            RiskLevel.Medium or RiskLevel.LowMedium => TimeSpan.FromMinutes(options.MediumTargetMinutes),
            _ => null,
        };
    }

    /// <summary>
    /// The chain at <paramref name="moment"/>. A call's target is the earlier of (call time + target for the latest score
    /// at or before the call) and (the first High score after the call + the High target): it tightens when the patient
    /// gets worse while waiting. Critical care is suggested when the score is High and critical care has not been called;
    /// otherwise the next tier is suggested once an open call's target has passed.
    /// </summary>
    public static EscalationState At(
        IReadOnlyList<EscalationRecord> records,
        IReadOnlyList<ScoreMark> scores,
        DateTimeOffset moment,
        EscalationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(scores);
        options ??= new EscalationOptions();

        var past = records.Where(r => r.At <= moment).OrderBy(r => r.At).ToList();
        var marks = scores.Where(s => s.At <= moment).OrderBy(s => s.At).ToList();

        var concern = past.OfType<ConcernRaised>().LastOrDefault();
        var patientConcern = past.OfType<PatientOrFamilyConcern>().LastOrDefault();

        var calls = past.OfType<Called>()
            .Select(call => BuildCall(call, past, marks, options))
            .ToList();

        var latest = calls.LastOrDefault();
        var step = latest is null ? EscalationStep.None
            : latest.PlannedAt is not null ? EscalationStep.Planned
            : latest.AssessedAt is not null ? EscalationStep.Assessed
            : latest.ArrivedAt is not null ? EscalationStep.Arrived
            : EscalationStep.Called;

        var open = calls.LastOrDefault(c => c.ArrivedAt is null);
        int? waited = open is null ? null : (int)Math.Floor((moment - open.CalledAt).TotalMinutes);
        var passed = open?.TargetAt is { } target && target < moment;

        var riskNow = marks.LastOrDefault()?.Risk;
        EscalationTier? suggestion =
            riskNow == RiskLevel.High && calls.All(c => c.Tier != EscalationTier.CriticalCare) ? EscalationTier.CriticalCare
            : passed && open!.Tier != EscalationTier.CriticalCare ? open.Tier + 1
            : null;

        return new EscalationState(
            step,
            concern?.At,
            concern?.Note,
            patientConcern?.At,
            calls,
            open,
            waited,
            passed,
            suggestion);
    }

    private static CallState BuildCall(Called call, List<EscalationRecord> past, List<ScoreMark> marks, EscalationOptions options)
    {
        var arrived = past.OfType<Arrived>().FirstOrDefault(r => r.CallId == call.CallId);
        var assessed = past.OfType<Assessed>().FirstOrDefault(r => r.CallId == call.CallId);
        var planned = past.OfType<Planned>().FirstOrDefault(r => r.CallId == call.CallId);

        DateTimeOffset? target = null;
        if (TargetFor(marks.LastOrDefault(m => m.At <= call.At)?.Risk, options) is { } atCall)
        {
            target = call.At + atCall;
        }

        if (marks.FirstOrDefault(m => m.At > call.At && m.Risk == RiskLevel.High) is { } high)
        {
            var tightened = high.At + TimeSpan.FromMinutes(options.HighTargetMinutes);
            target = target is null || tightened < target ? tightened : target;
        }

        return new CallState(
            call.CallId,
            call.Tier,
            call.At,
            target,
            arrived?.At,
            arrived?.Simulated ?? false,
            assessed?.At,
            assessed?.Text,
            planned?.At,
            planned?.Text);
    }
}
