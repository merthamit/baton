namespace Baton.Scenarios;

/// <summary>
/// Small seeded measurement noise, like real bedside readings: normal-distributed, capped at three
/// standard deviations. Oxygen flow and consciousness are never changed. With level 0 values pass through.
/// </summary>
internal static class MeasurementNoise
{
    private const double RespirationSd = 0.8, SpO2Sd = 0.6, SystolicSd = 3, PulseSd = 2.5, TemperatureSd = 0.08;

    public static VitalSet Apply(VitalSet vitals, Random random, double level)
    {
        if (level <= 0)
        {
            return vitals;
        }

        double Noise(double sd) => Math.Clamp(Gaussian(random), -3, 3) * sd * level;

        return vitals with
        {
            RespirationRate = Round(vitals.RespirationRate + Noise(RespirationSd)),
            SpO2 = Math.Min(100, Round(vitals.SpO2 + Noise(SpO2Sd))),
            SystolicBp = Round(vitals.SystolicBp + Noise(SystolicSd)),
            PulseRate = Round(vitals.PulseRate + Noise(PulseSd)),
            TemperatureCelsius = Math.Round(vitals.TemperatureCelsius + (decimal)Noise(TemperatureSd), 1, MidpointRounding.AwayFromZero),
        };
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // Box-Muller transform.
    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
