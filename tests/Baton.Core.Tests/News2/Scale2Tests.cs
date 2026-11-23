using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

public class Scale2Tests
{
    [Fact]
    public void Calculate_Scale2WithoutDecision_DoesNotScoreSpO2()
    {
        var input = TestInputs.Normal() with { SpO2Scale = SpO2Scale.Scale2, SpO2 = 90 };

        var result = News2Calculator.Calculate(input);

        Assert.False(result.IsComplete);
        Assert.Null(result.SubScores[News2Parameter.SpO2]);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(News2Parameter.SpO2, problem.Parameter);
        Assert.Equal(ProblemCode.Scale2NotAuthorised, problem.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Calculate_Scale2DecisionWithBlankAuthor_IsNotAuthorised(string author)
    {
        var input = TestInputs.Normal() with
        {
            SpO2Scale = SpO2Scale.Scale2,
            SpO2 = 90,
            Scale2Authorisation = TestInputs.ValidScale2Decision() with { AuthorisedBy = author },
        };

        var result = News2Calculator.Calculate(input);

        Assert.Null(result.SubScores[News2Parameter.SpO2]);
        Assert.Equal(ProblemCode.Scale2NotAuthorised, Assert.Single(result.Problems).Code);
    }

    [Fact]
    public void Calculate_Scale2WithDecisionOnOxygen_ScoresScale2()
    {
        var input = TestInputs.Normal() with
        {
            SpO2Scale = SpO2Scale.Scale2,
            SpO2 = 90,
            OnSupplementalOxygen = true,
            Scale2Authorisation = TestInputs.ValidScale2Decision(),
        };

        var result = News2Calculator.Calculate(input);

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.SubScores[News2Parameter.SpO2]);
        Assert.Equal(2, result.SubScores[News2Parameter.SupplementalOxygen]);
    }

    [Fact]
    public void Calculate_Scale2OxygenUnknownAndSpO2AtMost92_StillScoresSpO2()
    {
        var input = TestInputs.Normal() with
        {
            SpO2Scale = SpO2Scale.Scale2,
            SpO2 = 90,
            OnSupplementalOxygen = null,
            Scale2Authorisation = TestInputs.ValidScale2Decision(),
        };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(0, result.SubScores[News2Parameter.SpO2]);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(News2Parameter.SupplementalOxygen, problem.Parameter);
        Assert.Equal(ProblemCode.Missing, problem.Code);
    }

    [Fact]
    public void Calculate_Scale2OxygenUnknownAndSpO2Above92_CannotScoreSpO2()
    {
        var input = TestInputs.Normal() with
        {
            SpO2Scale = SpO2Scale.Scale2,
            SpO2 = 95,
            OnSupplementalOxygen = null,
            Scale2Authorisation = TestInputs.ValidScale2Decision(),
        };

        var result = News2Calculator.Calculate(input);

        Assert.Null(result.SubScores[News2Parameter.SpO2]);
        Assert.Null(result.SubScores[News2Parameter.SupplementalOxygen]);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(News2Parameter.SupplementalOxygen, problem.Parameter);
    }

    [Fact]
    public void Calculate_Scale1WithDecisionPresent_IgnoresDecisionAndUsesScale1()
    {
        var input = TestInputs.Normal() with
        {
            SpO2Scale = SpO2Scale.Scale1,
            SpO2 = 90,
            Scale2Authorisation = TestInputs.ValidScale2Decision(),
        };

        var result = News2Calculator.Calculate(input);

        Assert.True(result.IsComplete);
        Assert.Equal(3, result.SubScores[News2Parameter.SpO2]);
        Assert.Empty(result.Problems);
    }
}
