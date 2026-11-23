using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

// Every whole number in each plausible range (and every 0.1 °C step) against an independent
// transcription of the RCP chart in RcpReferenceTable.
public class SweepTests
{
    public static TheoryData<int> RespirationRates => Range(1, 80);
    public static TheoryData<int> SpO2Values => Range(1, 100);
    public static TheoryData<int> SystolicValues => Range(1, 300);
    public static TheoryData<int> PulseRates => Range(1, 300);

    public static TheoryData<decimal> Temperatures
    {
        get
        {
            var data = new TheoryData<decimal>();
            for (var t = 25.0m; t <= 45.0m; t += 0.1m)
            {
                data.Add(t);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RespirationRates))]
    public void RespirationRate_MatchesRcpTable(int value) =>
        AssertSubScore(TestInputs.Normal() with { RespirationRate = value }, News2Parameter.RespirationRate, value);

    [Theory]
    [MemberData(nameof(SpO2Values))]
    public void SpO2Scale1_MatchesRcpTable(int value) =>
        AssertSubScore(TestInputs.Normal() with { SpO2 = value }, News2Parameter.SpO2, value);

    [Theory]
    [MemberData(nameof(SystolicValues))]
    public void SystolicBp_MatchesRcpTable(int value) =>
        AssertSubScore(TestInputs.Normal() with { SystolicBp = value }, News2Parameter.SystolicBp, value);

    [Theory]
    [MemberData(nameof(PulseRates))]
    public void PulseRate_MatchesRcpTable(int value) =>
        AssertSubScore(TestInputs.Normal() with { PulseRate = value }, News2Parameter.PulseRate, value);

    [Theory]
    [MemberData(nameof(Temperatures))]
    public void Temperature_MatchesRcpTable(decimal value) =>
        AssertSubScore(TestInputs.Normal() with { TemperatureCelsius = value }, News2Parameter.Temperature, value);

    private static void AssertSubScore(News2Input input, News2Parameter parameter, decimal value)
    {
        var result = News2Calculator.Calculate(input);

        Assert.Equal(RcpReferenceTable.ExpectedPoints(parameter, value), result.SubScores[parameter]);
    }

    private static TheoryData<int> Range(int from, int to)
    {
        var data = new TheoryData<int>();
        for (var i = from; i <= to; i++)
        {
            data.Add(i);
        }

        return data;
    }
}
