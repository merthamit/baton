using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

public class ValidationTests
{
    public static TheoryData<News2Parameter, News2Input> EachFieldMissing => new()
    {
        { News2Parameter.RespirationRate, TestInputs.Normal() with { RespirationRate = null } },
        { News2Parameter.SpO2, TestInputs.Normal() with { SpO2 = null } },
        { News2Parameter.SupplementalOxygen, TestInputs.Normal() with { OnSupplementalOxygen = null } },
        { News2Parameter.SystolicBp, TestInputs.Normal() with { SystolicBp = null } },
        { News2Parameter.PulseRate, TestInputs.Normal() with { PulseRate = null } },
        { News2Parameter.Consciousness, TestInputs.Normal() with { Consciousness = null } },
        { News2Parameter.Temperature, TestInputs.Normal() with { TemperatureCelsius = null } },
    };

    [Theory]
    [MemberData(nameof(EachFieldMissing))]
    public void Calculate_OneFieldMissing_ReportsOnlyThatParameterAndScoresTheRest(News2Parameter missing, News2Input input)
    {
        var result = News2Calculator.Calculate(input);

        Assert.False(result.IsComplete);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(missing, problem.Parameter);
        Assert.Equal(ProblemCode.Missing, problem.Code);
        Assert.Null(result.SubScores[missing]);
        Assert.All(result.SubScores.Where(kv => kv.Key != missing), kv => Assert.Equal(0, kv.Value));
    }

    public static TheoryData<News2Parameter, News2Input> ImplausibleValues => new()
    {
        { News2Parameter.RespirationRate, TestInputs.Normal() with { RespirationRate = 0 } },
        { News2Parameter.RespirationRate, TestInputs.Normal() with { RespirationRate = 81 } },
        { News2Parameter.SpO2, TestInputs.Normal() with { SpO2 = 0 } },
        { News2Parameter.SpO2, TestInputs.Normal() with { SpO2 = 101 } },
        { News2Parameter.SystolicBp, TestInputs.Normal() with { SystolicBp = 0 } },
        { News2Parameter.SystolicBp, TestInputs.Normal() with { SystolicBp = 301 } },
        { News2Parameter.PulseRate, TestInputs.Normal() with { PulseRate = 0 } },
        { News2Parameter.PulseRate, TestInputs.Normal() with { PulseRate = 301 } },
        { News2Parameter.Temperature, TestInputs.Normal() with { TemperatureCelsius = 24.9m } },
        { News2Parameter.Temperature, TestInputs.Normal() with { TemperatureCelsius = 45.1m } },
    };

    [Theory]
    [MemberData(nameof(ImplausibleValues))]
    public void Calculate_ImplausibleValue_IsNotScored(News2Parameter parameter, News2Input input)
    {
        var result = News2Calculator.Calculate(input);

        Assert.False(result.IsComplete);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(parameter, problem.Parameter);
        Assert.Equal(ProblemCode.ImplausibleValue, problem.Code);
        Assert.Null(result.SubScores[parameter]);
    }

    public static TheoryData<News2Parameter, News2Input, int> EdgeOfPlausibleRange => new()
    {
        { News2Parameter.RespirationRate, TestInputs.Normal() with { RespirationRate = 1 }, 3 },
        { News2Parameter.RespirationRate, TestInputs.Normal() with { RespirationRate = 80 }, 3 },
        { News2Parameter.SpO2, TestInputs.Normal() with { SpO2 = 1 }, 3 },
        { News2Parameter.SpO2, TestInputs.Normal() with { SpO2 = 100 }, 0 },
        { News2Parameter.SystolicBp, TestInputs.Normal() with { SystolicBp = 1 }, 3 },
        { News2Parameter.SystolicBp, TestInputs.Normal() with { SystolicBp = 300 }, 3 },
        { News2Parameter.PulseRate, TestInputs.Normal() with { PulseRate = 1 }, 3 },
        { News2Parameter.PulseRate, TestInputs.Normal() with { PulseRate = 300 }, 3 },
        { News2Parameter.Temperature, TestInputs.Normal() with { TemperatureCelsius = 25.0m }, 3 },
        { News2Parameter.Temperature, TestInputs.Normal() with { TemperatureCelsius = 45.0m }, 2 },
    };

    [Theory]
    [MemberData(nameof(EdgeOfPlausibleRange))]
    public void Calculate_ValueAtEdgeOfPlausibleRange_IsScored(News2Parameter parameter, News2Input input, int expected)
    {
        var result = News2Calculator.Calculate(input);

        Assert.True(result.IsComplete);
        Assert.Equal(expected, result.SubScores[parameter]);
    }

    [Fact]
    public void Calculate_UndefinedConsciousnessValue_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            News2Calculator.Calculate(TestInputs.Normal() with { Consciousness = (Consciousness)99 }));

    [Fact]
    public void Calculate_UndefinedSpO2Scale_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            News2Calculator.Calculate(TestInputs.Normal() with { SpO2Scale = (SpO2Scale)7 }));

    [Fact]
    public void Calculate_AlertWithConfusionOnsetSet_ScoresZero()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { ConfusionOnset = ConfusionOnset.New });

        Assert.Equal(0, result.SubScores[News2Parameter.Consciousness]);
    }
}
