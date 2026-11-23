namespace Baton.Scenarios;

/// <summary>
/// The patient's true condition from a card: numbers change linearly between keyframes; oxygen flow and
/// consciousness change in steps (they keep the previous keyframe's value until the next keyframe).
/// </summary>
public static class TrueState
{
    public static VitalSet At(ScenarioCard card, double hour)
    {
        ArgumentNullException.ThrowIfNull(card);
        var frames = card.Keyframes;
        if (frames.Count == 0)
        {
            throw new ArgumentException("A scenario card needs at least one keyframe.", nameof(card));
        }

        if (hour <= frames[0].Hour)
        {
            return frames[0].Vitals;
        }

        for (var i = 1; i < frames.Count; i++)
        {
            var (a, b) = (frames[i - 1], frames[i]);
            if (hour > b.Hour)
            {
                continue;
            }

            if (hour == b.Hour)
            {
                return b.Vitals;
            }

            var k = (hour - a.Hour) / (b.Hour - a.Hour);
            return new VitalSet(
                Lerp(a.Vitals.RespirationRate, b.Vitals.RespirationRate, k),
                Lerp(a.Vitals.SpO2, b.Vitals.SpO2, k),
                a.Vitals.OxygenLitresPerMinute,
                Lerp(a.Vitals.SystolicBp, b.Vitals.SystolicBp, k),
                Lerp(a.Vitals.PulseRate, b.Vitals.PulseRate, k),
                Math.Round(a.Vitals.TemperatureCelsius + ((b.Vitals.TemperatureCelsius - a.Vitals.TemperatureCelsius) * (decimal)k), 1, MidpointRounding.AwayFromZero),
                a.Vitals.Consciousness);
        }

        return frames[^1].Vitals;
    }

    private static int Lerp(int a, int b, double k) => (int)Math.Round(a + ((b - a) * k), MidpointRounding.AwayFromZero);
}
