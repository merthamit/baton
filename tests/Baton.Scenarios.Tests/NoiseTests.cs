namespace Baton.Scenarios.Tests;

public class NoiseTests
{
    private static SimulatedPatient Run(int seed, double level = 1) =>
        ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet with { Seed = seed, NoiseLevel = level });

    [Fact]
    public void SameSeed_GivesTheSameShift()
    {
        Assert.Equal(Run(42).Measurements.Select(m => (m.Hour, m.Vitals)), Run(42).Measurements.Select(m => (m.Hour, m.Vitals)));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentValues()
    {
        Assert.NotEqual(Run(1).Measurements.Select(m => m.Vitals), Run(2).Measurements.Select(m => m.Vitals));
    }

    [Fact]
    public void Noise_ChangesValuesOnlyALittle()
    {
        var quiet = ShiftSimulator.Run(ScenarioCards.Stable, CardFidelityTests.Quiet);
        foreach (var seed in Enumerable.Range(1, 50))
        {
            var noisy = ShiftSimulator.Run(ScenarioCards.Stable, CardFidelityTests.Quiet with { Seed = seed, NoiseLevel = 1 });
            foreach (var (q, n) in quiet.Measurements.Zip(noisy.Measurements))
            {
                Assert.InRange(n.Vitals.RespirationRate, q.Vitals.RespirationRate - 3, q.Vitals.RespirationRate + 3);
                Assert.InRange(n.Vitals.SpO2, q.Vitals.SpO2 - 3, 100);
                Assert.InRange(n.Vitals.SystolicBp, q.Vitals.SystolicBp - 12, q.Vitals.SystolicBp + 12);
                Assert.InRange(n.Vitals.PulseRate, q.Vitals.PulseRate - 10, q.Vitals.PulseRate + 10);
                Assert.InRange(n.Vitals.TemperatureCelsius, q.Vitals.TemperatureCelsius - 0.4m, q.Vitals.TemperatureCelsius + 0.4m);
                Assert.Equal(q.Vitals.OxygenLitresPerMinute, n.Vitals.OxygenLitresPerMinute);
                Assert.Equal(q.Vitals.Consciousness, n.Vitals.Consciousness);
            }
        }
    }

    [Fact]
    public void NoiseLevelZero_IsTheCardExactly()
    {
        Assert.Equal(
            ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet).Measurements.Select(m => m.Vitals),
            Run(42, level: 0).Measurements.Select(m => m.Vitals));
    }
}
