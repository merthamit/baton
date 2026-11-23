using Baton.Core.News2;

namespace Baton.Scenarios;

/// <summary>One full set of the seven NEWS2 vital signs. Oxygen is a flow in L/min; 0 means room air.</summary>
public sealed record VitalSet(
    int RespirationRate,
    int SpO2,
    decimal OxygenLitresPerMinute,
    int SystolicBp,
    int PulseRate,
    decimal TemperatureCelsius,
    Consciousness Consciousness)
{
    public News2Input ToNews2Input(SpO2Scale scale, Scale2Authorisation? scale2Authorisation) => new()
    {
        RespirationRate = RespirationRate,
        SpO2 = SpO2,
        SpO2Scale = scale,
        Scale2Authorisation = scale2Authorisation,
        OnSupplementalOxygen = OxygenLitresPerMinute > 0,
        SystolicBp = SystolicBp,
        PulseRate = PulseRate,
        TemperatureCelsius = TemperatureCelsius,
        Consciousness = Consciousness,
    };
}

/// <summary>The patient's true vital signs at an hour of the shift (0 = 08:00, 24 = next 08:00).</summary>
public sealed record Keyframe(double Hour, VitalSet Vitals);

public enum ScenarioEventKind
{
    /// <summary>Nurse "I am worried" flag. Triggers a measurement within 1 hour, then at least every 2 hours.</summary>
    Concern,

    /// <summary>A clinician records the decision to use SpO2 Scale 2.</summary>
    Scale2Decision,

    /// <summary>Baton raised an alert (NEWS2 5+ or a single parameter scoring 3).</summary>
    Alert,

    DoctorCalled,
    DoctorArrived,
    TreatmentStarted,
    CriticalCareCalled,
    Note,
}

public sealed record ScenarioEvent(double Hour, ScenarioEventKind Kind, string Text);

/// <summary>
/// What happens when treatment is late (decision 2B). The card's keyframes describe timely treatment
/// starting at <see cref="TreatmentHour"/>. Each hour of delay adds the penalties below; a delay longer
/// than 2 hours also makes the patient newly confused. After treatment the penalty fades to zero over
/// <see cref="RecoveryHours"/>.
/// </summary>
public sealed record DelayRule(
    double TreatmentHour,
    double RespirationPerHour,
    double SpO2PerHour,
    double SystolicPerHour,
    double PulsePerHour,
    double RecoveryHours);

/// <summary>A scenario card, written like a simulation-lab script.</summary>
public sealed record ScenarioCard(
    string Id,
    string Title,
    string Diagnosis,
    SpO2Scale Scale,
    IReadOnlyList<Keyframe> Keyframes,
    IReadOnlyList<ScenarioEvent> Events,
    DelayRule? Delay);
