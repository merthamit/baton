using Baton.Core.News2;

namespace Baton.Scenarios.Tests;

// With noise off, the simulator must reproduce the scenario cards the project owner approved (decisions page):
// the nurse measures at the card's times and NEWS2 at those times matches the card.
public class CardFidelityTests
{
    internal static readonly SimulationOptions Quiet = new(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3)));

    private static int ScoreAt(SimulatedPatient p, double hour) =>
        p.Measurements.Single(m => Math.Abs(m.Hour - hour) < 0.001).Score.Total!.Value;

    [Theory]
    [InlineData(0, 0)]      // 08:00
    [InlineData(12, 2)]     // 20:00
    [InlineData(13.25, 2)]  // 21:15, one hour after the 20:15 concern flag
    [InlineData(15.25, 6)]  // 23:15
    [InlineData(16.25, 10)] // 00:15
    [InlineData(19.25, 3)]  // 03:15
    public void Sepsis_MatchesCard(double hour, int score) =>
        Assert.Equal(score, ScoreAt(ShiftSimulator.Run(ScenarioCards.Sepsis, Quiet), hour));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 1)]    // 20:00
    [InlineData(14.5, 5)]  // 22:30, one hour after the 21:30 concern flag
    [InlineData(15.5, 7)]  // 23:30
    [InlineData(19.5, 1)]  // 03:30 (card said 02:30; the 3C schedule measures at 03:30)
    public void HiddenBleed_MatchesCard(double hour, int score) =>
        Assert.Equal(score, ScoreAt(ShiftSimulator.Run(ScenarioCards.HiddenBleed, Quiet), hour));

    [Theory]
    [InlineData(0, 3)]   // 08:00, Scale 2
    [InlineData(6, 5)]   // 14:00, one hour after the 13:00 concern flag
    [InlineData(8, 7)]   // 16:00, oxygen turned up to 4 L/min
    [InlineData(11, 3)]  // 19:00 (card said 20:00; the 3C schedule measures at 19:00)
    public void CopdScale2_MatchesCard(double hour, int score) =>
        Assert.Equal(score, ScoreAt(ShiftSimulator.Run(ScenarioCards.CopdScale2, Quiet), hour));

    [Fact]
    public void Recovering_MeasuresLessOftenAsTheScoreFalls()
    {
        var p = ShiftSimulator.Run(ScenarioCards.Recovering, Quiet);

        Assert.Equal([0, 1, 5, 9, 21], p.Measurements.Select(m => m.Hour));
        Assert.Equal([5, 4, 1, 0, 0], p.Measurements.Select(m => m.Score.Total!.Value));
    }

    [Fact]
    public void Stable_IsMeasuredTwelveHourlyAndScoresZero()
    {
        var p = ShiftSimulator.Run(ScenarioCards.Stable, Quiet);

        Assert.Equal([0, 12], p.Measurements.Select(m => m.Hour));
        Assert.All(p.Measurements, m => Assert.Equal(0, m.Score.Total));
    }

    [Fact]
    public void CopdScale2_UsesTheRecordedScale2Decision()
    {
        var first = ShiftSimulator.Run(ScenarioCards.CopdScale2, Quiet).Measurements[0];

        Assert.True(first.Score.IsComplete);
        Assert.Equal(SpO2Scale.Scale2, first.Input.SpO2Scale);
        Assert.NotNull(first.Input.Scale2Authorisation);
    }
}
