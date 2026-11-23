using System.Globalization;
using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

public class RoundingTests
{
    [Theory]
    [InlineData("35.04", "35.0", 3)]
    [InlineData("35.05", "35.1", 1)]
    [InlineData("38.05", "38.1", 1)]
    [InlineData("39.05", "39.1", 2)]
    [InlineData("38.049", "38.0", 0)]
    [InlineData("45.04", "45.0", 2)]
    public void Calculate_TemperatureWithMoreThanOneDecimal_IsRoundedHalfUpAndNoted(string raw, string rounded, int expected)
    {
        var input = TestInputs.Normal() with { TemperatureCelsius = Parse(raw) };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(expected, result.SubScores[News2Parameter.Temperature]);
        Assert.Contains($"Temperature rounded from {raw} to {rounded} °C", result.Notes);
    }

    [Fact]
    public void Calculate_TemperatureWithOneDecimal_HasNoRoundingNote()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { TemperatureCelsius = 37.0m });

        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Calculate_TemperatureRoundingAbove45_IsImplausible()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { TemperatureCelsius = 45.05m });

        Assert.Null(result.SubScores[News2Parameter.Temperature]);
        Assert.Equal(ProblemCode.ImplausibleValue, Assert.Single(result.Problems).Code);
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
