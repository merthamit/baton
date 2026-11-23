using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

// Regression tests for the findings of the final whole-branch review.
public class ReviewFindingTests
{
    [Fact]
    public void Calculate_Scale2WithoutDecisionAndSpO2AtMost83_StillFlagsSpO2AsRed()
    {
        // SpO2 ≤83 scores 3 on both scales, so its red status does not depend on the missing decision.
        var input = TestInputs.Normal() with { SpO2Scale = SpO2Scale.Scale2, SpO2 = 80, Scale2Authorisation = null };

        var result = News2Calculator.Calculate(input);

        Assert.Null(result.SubScores[News2Parameter.SpO2]);
        Assert.Equal(ProblemCode.Scale2NotAuthorised, Assert.Single(result.Problems).Code);
        Assert.True(result.HasSingleParameterScoreOf3);
        Assert.Equal([News2Parameter.SpO2], result.ParametersScoring3);
    }

    [Fact]
    public void Calculate_Scale2WithoutDecisionAndSpO2Of84_IsNotFlaggedRed()
    {
        // 84 scores 2 on Scale 2 but 3 on Scale 1: ambiguous, so it is not reported as red.
        var input = TestInputs.Normal() with { SpO2Scale = SpO2Scale.Scale2, SpO2 = 84, Scale2Authorisation = null };

        var result = News2Calculator.Calculate(input);

        Assert.False(result.HasSingleParameterScoreOf3);
    }

    [Fact]
    public void Calculate_Incomplete_ReportsMinimumTotalOfScoredParameters()
    {
        // RR 21 → 2, SpO2 92 → 2, SBP 100 → 2, pulse 111 → 2; temperature missing: at least 8.
        var input = TestInputs.Normal() with
        {
            RespirationRate = 21, SpO2 = 92, SystolicBp = 100, PulseRate = 111, TemperatureCelsius = null,
        };

        var result = News2Calculator.Calculate(input);

        Assert.False(result.IsComplete);
        Assert.Null(result.Total);
        Assert.Equal(8, result.MinimumTotal);
    }

    [Fact]
    public void Calculate_Complete_HasNoMinimumTotal()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal());

        Assert.Null(result.MinimumTotal);
    }

    [Fact]
    public void Calculate_Scale2OxygenUnknownAndSpO2Above92_ExplainsWhySpO2IsBlank()
    {
        var input = TestInputs.Normal() with
        {
            SpO2Scale = SpO2Scale.Scale2,
            SpO2 = 95,
            OnSupplementalOxygen = null,
            Scale2Authorisation = TestInputs.ValidScale2Decision(),
        };

        var result = News2Calculator.Calculate(input);

        var problem = Assert.Single(result.Problems);
        Assert.Contains("SpO2 ≥93% on Scale 2 cannot be scored until air or oxygen is recorded", problem.Message);
    }
}
