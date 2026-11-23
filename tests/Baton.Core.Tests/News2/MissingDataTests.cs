using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

public class MissingDataTests
{
    [Fact]
    public void Calculate_AllFieldsMissing_IsIncompleteWithSevenMissingProblems()
    {
        var result = News2Calculator.Calculate(new News2Input());

        Assert.False(result.IsComplete);
        Assert.Null(result.Total);
        Assert.Null(result.Risk);
        Assert.Equal(7, result.Problems.Count(p => p.Code == ProblemCode.Missing));
        Assert.All(result.SubScores.Values, Assert.Null);
        Assert.Equal("RCP-NEWS2-2017", result.AlgorithmVersion);
    }

    [Fact]
    public void Calculate_NullInput_Throws() =>
        Assert.Throws<ArgumentNullException>(() => News2Calculator.Calculate(null!));
}
