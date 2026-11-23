using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

// Rules that must hold for every valid input, checked on 2000 seeded random observation sets.
public class PropertyTests
{
    private const int Seed = 20261123;
    private const int Cases = 2000;

    [Fact]
    public void Total_AlwaysEqualsSumOfSubScores()
    {
        foreach (var input in RandomInputs())
        {
            var result = News2Calculator.Calculate(input);

            Assert.True(result.IsComplete, input.ToString());
            Assert.Equal(result.SubScores.Values.Sum(v => v!.Value), result.Total);
        }
    }

    [Fact]
    public void AddingOxygen_NeverLowersTheTotal()
    {
        foreach (var input in RandomInputs())
        {
            var onAir = News2Calculator.Calculate(input with { OnSupplementalOxygen = false });
            var onOxygen = News2Calculator.Calculate(input with { OnSupplementalOxygen = true });

            Assert.True(onOxygen.Total >= onAir.Total, input.ToString());
        }
    }

    [Fact]
    public void TotalAtMostFourWithAParameterScoring3_IsAlwaysLowMedium()
    {
        foreach (var input in RandomInputs())
        {
            var result = News2Calculator.Calculate(input);

            if (result.Total <= 4 && result.HasSingleParameterScoreOf3)
            {
                Assert.Equal(RiskLevel.LowMedium, result.Risk);
            }
        }
    }

    [Fact]
    public void Calculate_IsDeterministic()
    {
        foreach (var input in RandomInputs())
        {
            var first = News2Calculator.Calculate(input);
            var second = News2Calculator.Calculate(input);

            Assert.Equal(first.Total, second.Total);
            Assert.Equal(first.Risk, second.Risk);
            Assert.Equal(first.SubScores, second.SubScores);
        }
    }

    private static IEnumerable<News2Input> RandomInputs()
    {
        var random = new Random(Seed);
        for (var i = 0; i < Cases; i++)
        {
            var scale = random.Next(2) == 0 ? SpO2Scale.Scale1 : SpO2Scale.Scale2;
            yield return new News2Input
            {
                RespirationRate = random.Next(1, 81),
                SpO2 = random.Next(1, 101),
                SpO2Scale = scale,
                Scale2Authorisation = scale == SpO2Scale.Scale2 ? TestInputs.ValidScale2Decision() : null,
                OnSupplementalOxygen = random.Next(2) == 0,
                SystolicBp = random.Next(1, 301),
                PulseRate = random.Next(1, 301),
                TemperatureCelsius = 25.0m + (random.Next(0, 201) / 10m),
                Consciousness = (Consciousness)random.Next(0, 5),
                ConfusionOnset = (ConfusionOnset)random.Next(0, 3),
            };
        }
    }
}
