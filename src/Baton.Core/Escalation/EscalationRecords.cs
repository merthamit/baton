using Baton.Core.News2;

namespace Baton.Core.Escalation;

/// <summary>Who is called, in escalation order.</summary>
public enum EscalationTier
{
    OnCallDoctor,
    SeniorDoctor,
    CriticalCare,
}

/// <summary>How far the latest call has got.</summary>
public enum EscalationStep
{
    None,
    Called,
    Arrived,
    Assessed,
    Planned,
}

/// <summary>
/// One thing that happened in a patient's escalation chain. NEWS2 guidance asks for the time of escalation, the time and
/// grade of the response, the assessment and the plan to be recorded; these records are exactly that.
/// </summary>
/// <param name="At">When it happened.</param>
/// <param name="By">Who did it, as a FHIR reference (for example <c>Practitioner/synthetic-nurse</c>).</param>
public abstract record EscalationRecord(DateTimeOffset At, string By);

/// <summary>A nurse's "I am worried about this patient" flag.</summary>
public sealed record ConcernRaised(DateTimeOffset At, string By, string Note) : EscalationRecord(At, By);

/// <summary>A patient or a family member says they are worried.</summary>
public sealed record PatientOrFamilyConcern(DateTimeOffset At, string By, string Text) : EscalationRecord(At, By);

/// <summary>A clinician of <paramref name="Tier"/> is called. Later steps of this call carry the same <paramref name="CallId"/>.</summary>
public sealed record Called(DateTimeOffset At, string By, string CallId, EscalationTier Tier) : EscalationRecord(At, By);

/// <summary>The called clinician arrived. <paramref name="Simulated"/> marks an arrival Baton assumed rather than recorded.</summary>
public sealed record Arrived(DateTimeOffset At, string By, string CallId, bool Simulated) : EscalationRecord(At, By);

/// <summary>The clinical assessment for a call.</summary>
public sealed record Assessed(DateTimeOffset At, string By, string CallId, string Text) : EscalationRecord(At, By);

/// <summary>The plan made for a call.</summary>
public sealed record Planned(DateTimeOffset At, string By, string CallId, string Text) : EscalationRecord(At, By);

/// <summary>The NEWS2 risk at a moment, used to set and tighten call targets.</summary>
public sealed record ScoreMark(DateTimeOffset At, RiskLevel? Risk);

/// <summary>Response targets in minutes (RCP NEWS2: 5–6 within 60, 7+ within 30). Configurable defaults.</summary>
public sealed class EscalationOptions
{
    /// <summary>For risk Medium (5–6) and, Baton's assumption, LowMedium (a single parameter scoring 3).</summary>
    public int MediumTargetMinutes { get; init; } = 60;

    /// <summary>For risk High (7+).</summary>
    public int HighTargetMinutes { get; init; } = 30;
}

/// <summary>One call and how far it has got.</summary>
public sealed record CallState(
    string CallId,
    EscalationTier Tier,
    DateTimeOffset CalledAt,
    DateTimeOffset? TargetAt,
    DateTimeOffset? ArrivedAt,
    bool ArrivalSimulated,
    DateTimeOffset? AssessedAt,
    string? Assessment,
    DateTimeOffset? PlannedAt,
    string? Plan);

/// <summary>A patient's escalation chain at a moment. Baton reports; it never calls anyone.</summary>
public sealed record EscalationState(
    EscalationStep Step,
    DateTimeOffset? ConcernRaisedAt,
    string? ConcernNote,
    DateTimeOffset? PatientConcernAt,
    IReadOnlyList<CallState> Calls,
    CallState? OpenCall,
    int? MinutesWaited,
    bool TargetPassed,
    EscalationTier? SuggestedNextTier)
{
    public static EscalationState None { get; } = new(EscalationStep.None, null, null, null, [], null, null, false, null);
}
