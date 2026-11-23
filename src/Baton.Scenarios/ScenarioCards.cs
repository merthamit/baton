using Baton.Core.News2;

namespace Baton.Scenarios;

/// <summary>
/// The five scenario cards from the design (docs/superpowers/specs/2026-11-24-scenario-generator-design.md).
/// Hours are from 08:00. Values: respiration / SpO2 / oxygen L/min / systolic / pulse / temperature / ACVPU.
/// Keyframes at measurement times hold the exact values of the approved cards; the cards were checked
/// against the RCP NEWS2 2017 chart and the sources listed in the design.
/// </summary>
public static class ScenarioCards
{
    private const Consciousness A = Consciousness.Alert;

    private static Keyframe K(double hour, int rr, int spo2, decimal o2, int sbp, int hr, decimal temp, Consciousness c = A) =>
        new(hour, new VitalSet(rr, spo2, o2, sbp, hr, temp, c));

    private static ScenarioEvent E(double hour, ScenarioEventKind kind, string text) => new(hour, kind, text);

    /// <summary>Quiet sepsis: pneumonia, deterioration from 18:00, concern 20:15, antibiotics 00:05.</summary>
    public static readonly ScenarioCard Sepsis = new(
        "sepsis", "Quiet sepsis", "Community-acquired pneumonia", SpO2Scale.Scale1,
        [
            K(0, 16, 97, 0, 128, 78, 37.1m),
            K(10, 16, 97, 0, 128, 78, 37.1m),
            K(12, 19, 96, 0, 118, 96, 38.3m),
            K(13.25, 20, 96, 0, 114, 100, 38.5m),
            K(15.25, 22, 95, 0, 108, 106, 38.7m),
            K(16.25, 24, 94, 2, 100, 116, 38.9m),
            K(19.25, 20, 96, 2, 114, 96, 38.0m),
            K(24, 18, 97, 2, 118, 88, 37.4m),
        ],
        [
            E(12.25, ScenarioEventKind.Concern, "Restless, not eating, not herself"),
            E(15 + (40 / 60.0), ScenarioEventKind.DoctorArrived, "On-call doctor: suspected sepsis"),
            E(16 + (5 / 60.0), ScenarioEventKind.TreatmentStarted, "Blood cultures, lactate, IV fluids, oxygen 2 L/min, antibiotics"),
        ],
        new DelayRule(TreatmentHour: 16 + (5 / 60.0), RespirationPerHour: 1, SpO2PerHour: -1, SystolicPerHour: -5, PulsePerHour: 5, RecoveryHours: 3));

    /// <summary>Hidden upper GI bleed: no fever; tachycardia and falling blood pressure.</summary>
    public static readonly ScenarioCard HiddenBleed = new(
        "bleed", "Hidden bleed", "Upper GI bleed under observation", SpO2Scale.Scale1,
        [
            K(0, 16, 97, 0, 132, 80, 36.6m),
            K(6, 16, 97, 0, 132, 80, 36.6m),
            K(12, 18, 97, 0, 118, 96, 36.5m),
            K(14.5, 21, 96, 0, 104, 112, 36.3m),
            K(15.5, 23, 95, 0, 94, 124, 36.2m),
            K(18.5, 18, 97, 0, 112, 98, 36.5m),
            K(24, 16, 97, 0, 120, 88, 36.6m),
        ],
        [
            E(13.5, ScenarioEventKind.Concern, "Pale, clammy, melaena"),
            E(14 + (50 / 60.0), ScenarioEventKind.DoctorArrived, "Blatchford score, two cannulas, group and save, fluids"),
            E(15.75, ScenarioEventKind.TreatmentStarted, "Transfusion by haemoglobin; urgent endoscopy after resuscitation"),
        ],
        new DelayRule(TreatmentHour: 15.75, RespirationPerHour: 1, SpO2PerHour: 0, SystolicPerHour: -6, PulsePerHour: 6, RecoveryHours: 3));

    /// <summary>COPD exacerbation on Scale 2 (hypercapnia confirmed on a previous admission), with the oxygen trap.</summary>
    public static readonly ScenarioCard CopdScale2 = new(
        "copd", "COPD exacerbation (Scale 2)", "COPD, hypercapnic respiratory failure", SpO2Scale.Scale2,
        [
            K(0, 20, 90, 1, 136, 92, 37.0m),
            K(4, 20, 90, 1, 136, 92, 37.0m),
            K(6, 24, 88, 2, 134, 102, 37.6m),
            K(8, 22, 96, 4, 132, 98, 37.4m),
            K(9, 21, 91, 2, 133, 96, 37.3m),
            K(12, 20, 90, 2, 134, 94, 37.2m),
            K(24, 19, 90, 1, 134, 90, 37.0m),
        ],
        [
            E(-0.5, ScenarioEventKind.Scale2Decision, "Scale 2, target 88-92%: hypercapnia confirmed on previous admission blood gas"),
            E(5, ScenarioEventKind.Concern, "More breathless, pausing between sentences"),
            E(6.5, ScenarioEventKind.DoctorArrived, "Doctor: nebulisers and steroid; do not increase oxygen"),
            E(6.5, ScenarioEventKind.TreatmentStarted, "Nebulised bronchodilator and steroid"),
            E(8, ScenarioEventKind.Note, "Oxygen turned up to 4 L/min; Scale 2 scores the high saturation"),
            E(8.5, ScenarioEventKind.Note, "Blood gas requested; oxygen back to 2 L/min"),
        ],
        new DelayRule(TreatmentHour: 6.5, RespirationPerHour: 2, SpO2PerHour: -1, SystolicPerHour: 0, PulsePerHour: 3, RecoveryHours: 5));

    /// <summary>Recovering pyelonephritis on day 2 of antibiotics: scores fall and measurements get less frequent.</summary>
    public static readonly ScenarioCard Recovering = new(
        "recovering", "Recovering patient", "Acute pyelonephritis, antibiotics day 2", SpO2Scale.Scale1,
        [
            K(0, 22, 95, 0, 112, 104, 38.4m),
            K(1, 21, 96, 0, 114, 100, 38.1m),
            K(5, 19, 96, 0, 118, 94, 37.6m),
            K(9, 17, 97, 0, 122, 86, 37.2m),
            K(24, 16, 97, 0, 124, 80, 36.9m),
        ],
        [
            E(0, ScenarioEventKind.Note, "Antibiotics day 2; fever usually settles within 1.5-3 days"),
            E(0.5, ScenarioEventKind.DoctorArrived, "Ward doctor: responding to antibiotics; continue, paracetamol, recheck hourly"),
        ],
        null);

    /// <summary>Stable control patient: nothing happens.</summary>
    public static readonly ScenarioCard Stable = new(
        "stable", "Stable patient", "Day 1 after laparoscopic cholecystectomy", SpO2Scale.Scale1,
        [
            K(0, 16, 97, 0, 126, 76, 36.8m),
            K(12, 17, 97, 0, 122, 80, 37.0m),
            K(24, 16, 97, 0, 124, 76, 36.8m),
        ],
        [],
        null);

    public static IReadOnlyList<ScenarioCard> All { get; } = [Sepsis, HiddenBleed, CopdScale2, Recovering, Stable];
}
