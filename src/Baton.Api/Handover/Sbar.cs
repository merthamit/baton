using Baton.Core.Escalation;
using Baton.Core.News2;

namespace Baton.Api.Handover;

/// <summary>The four parts of an SBAR handover, in order.</summary>
public enum SbarPart
{
    Situation,
    Background,
    Assessment,
    Recommendation,
}

/// <summary>Something the record should hold for a safe handover but does not.</summary>
public enum MissingKind
{
    NoObservations,
    NoCallAfterScore,
    NoCriticalCareCall,
    NoArrival,
    NoAssessment,
    NoPlan,
    ObservationOverdue,
}

/// <summary>Where a sentence comes from: a FHIR reference, the time of the record and a short label to show.</summary>
public sealed record SourceRef(string Reference, DateTimeOffset At, string Label);

public sealed record SbarSentence(string Text, IReadOnlyList<SourceRef> Sources);

public sealed record SbarSection(SbarPart Part, IReadOnlyList<SbarSentence> Sentences);

public sealed record MissingItem(MissingKind Kind, string Text, IReadOnlyList<SourceRef> Sources);

/// <summary>
/// One patient's rule-based handover at <see cref="At"/>, covering <see cref="WindowStart"/> to <see cref="At"/>.
/// Sentences are rendered in <see cref="Language"/> ("en" or "tr"); the stored document carries exactly these texts.
/// <see cref="FloorRisk"/> is the current risk, or for an incomplete set the least risk it already shows (its minimum
/// total, a parameter scoring 3), so an incomplete set never sorts below a calm one.
/// </summary>
public sealed record Sbar(
    DateTimeOffset At,
    DateTimeOffset WindowStart,
    string Language,
    IReadOnlyList<SbarSection> Sections,
    IReadOnlyList<MissingItem> Missing,
    string StateLine,
    int? CurrentTotal,
    RiskLevel? CurrentRisk,
    int? PeakTotal,
    RiskLevel? FloorRisk);

/// <summary>Everything the builder reads for one patient. Records and points must already be limited to <c>At</c>.</summary>
public sealed record SbarInput(
    PatientRef Patient,
    int? Age,
    IReadOnlyList<ScorePoint> Points,
    IReadOnlyList<EscalationRecord> Records,
    DateTimeOffset At,
    DateTimeOffset ShiftStart,
    DateTimeOffset? NextDue,
    int? OverdueMinutes,
    EscalationOptions Options);
