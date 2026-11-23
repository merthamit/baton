using Baton.Core.News2;

namespace Baton.Scenarios;

/// <summary>
/// Applies a card's <see cref="DelayRule"/> (decision 2B). The card describes timely treatment at
/// <see cref="DelayRule.TreatmentHour"/>. With a delay of d hours:
/// <list type="bullet">
/// <item>during the delay the patient stays at the treatment-moment state and worsens by the per-hour penalties;</item>
/// <item>after the late treatment the card's story resumes d hours later, and the accumulated penalty fades to
/// zero over <see cref="DelayRule.RecoveryHours"/>;</item>
/// <item>a delay of more than 2 hours makes the patient newly confused until the recovery is over.</item>
/// </list>
/// With no delay (or no rule) the card is returned unchanged.
/// </summary>
internal static class Delay
{
    private const double ConfusionAfterHours = 2;

    public static VitalSet Apply(ScenarioCard card, double hour, double delayHours)
    {
        var rule = card.Delay;
        if (rule is null || delayHours <= 0 || hour <= rule.TreatmentHour)
        {
            return TrueState.At(card, hour);
        }

        var t = rule.TreatmentHour;
        var lateTreatment = t + delayHours;
        VitalSet baseline;
        double penaltyHours;
        if (hour < lateTreatment)
        {
            baseline = TrueState.At(card, t);
            penaltyHours = hour - t;
        }
        else
        {
            baseline = TrueState.At(card, hour - delayHours);
            penaltyHours = delayHours * Math.Max(0, 1 - ((hour - lateTreatment) / rule.RecoveryHours));
        }

        var confused = delayHours > ConfusionAfterHours
            && hour - t > ConfusionAfterHours
            && hour < lateTreatment + rule.RecoveryHours;

        return baseline with
        {
            RespirationRate = Adjust(baseline.RespirationRate, rule.RespirationPerHour, penaltyHours, 6, 45),
            SpO2 = Adjust(baseline.SpO2, rule.SpO2PerHour, penaltyHours, 70, 100),
            SystolicBp = Adjust(baseline.SystolicBp, rule.SystolicPerHour, penaltyHours, 60, 230),
            PulseRate = Adjust(baseline.PulseRate, rule.PulsePerHour, penaltyHours, 35, 180),
            Consciousness = confused ? Consciousness.Confusion : baseline.Consciousness,
        };
    }

    private static int Adjust(int value, double perHour, double hours, int min, int max) =>
        (int)Math.Clamp(Math.Round(value + (perHour * hours), MidpointRounding.AwayFromZero), min, max);
}
