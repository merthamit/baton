namespace Baton.Core.News2;

/// <summary>Which NEWS2 oxygen saturation scale applies (RCP NEWS2 2017).</summary>
public enum SpO2Scale
{
    /// <summary>The default scale for all patients.</summary>
    Scale1,

    /// <summary>
    /// Only for patients with hypercapnic respiratory failure and a prescribed 88–92% target.
    /// Requires a recorded <see cref="Scale2Authorisation"/>.
    /// </summary>
    Scale2,
}

/// <summary>Level of consciousness on the ACVPU scale.</summary>
public enum Consciousness
{
    Alert,
    Confusion,
    Voice,
    Pain,
    Unresponsive,
}

/// <summary>Whether confusion is new. RCP NEWS2 2017: if in doubt, treat it as new.</summary>
public enum ConfusionOnset
{
    /// <summary>Not recorded. Scored as new confusion.</summary>
    Unknown,

    New,

    /// <summary>Known, long-standing confusion. Not scored.</summary>
    Chronic,
}

/// <summary>The seven NEWS2 parameters.</summary>
public enum News2Parameter
{
    RespirationRate,
    SpO2,
    SupplementalOxygen,
    SystolicBp,
    PulseRate,
    Consciousness,
    Temperature,
}

/// <summary>Why a parameter could not be scored.</summary>
public enum ProblemCode
{
    Missing,
    ImplausibleValue,
    Scale2NotAuthorised,
}

/// <summary>NEWS2 clinical risk.</summary>
public enum RiskLevel
{
    Low,

    /// <summary>A score of 3 in any single parameter, with a total of 4 or less.</summary>
    LowMedium,

    Medium,
    High,
}

/// <summary>The RCP NEWS2 2017 clinical response to a score.</summary>
public enum ClinicalResponse
{
    /// <summary>Total 0.</summary>
    RoutineMonitoring,

    /// <summary>Total 1–4: a registered nurse assesses and decides on monitoring and escalation.</summary>
    WardBasedResponse,

    /// <summary>A score of 3 in a single parameter: urgent review by a ward-based doctor.</summary>
    UrgentWardBasedResponse,

    /// <summary>Total 5–6: urgent review by a clinician competent in acute illness.</summary>
    UrgentResponse,

    /// <summary>Total 7 or more: emergency assessment by a team with critical care competencies.</summary>
    EmergencyResponse,
}
