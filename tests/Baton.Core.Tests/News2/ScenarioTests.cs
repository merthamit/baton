using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

// The four presets of the research report's calculator. Expected totals were worked out by hand
// from the RCP NEWS2 2017 chart; the per-parameter sums are in the comments.
public class ScenarioTests
{
    [Fact]
    public void StablePatient_ScoresZero()
    {
        var input = new News2Input
        {
            RespirationRate = 16, SpO2 = 97, OnSupplementalOxygen = false, SystolicBp = 124,
            PulseRate = 78, TemperatureCelsius = 36.8m, Consciousness = Consciousness.Alert,
        };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(0, result.Total);
        Assert.Equal(RiskLevel.Low, result.Risk);
    }

    [Fact]
    public void QuietSepsis_ScoresSeven()
    {
        // RR 23 → 2, SpO2 94 → 1, air → 0, SBP 102 → 1, HR 112 → 2, 38.6 °C → 1, Alert → 0
        var input = new News2Input
        {
            RespirationRate = 23, SpO2 = 94, OnSupplementalOxygen = false, SystolicBp = 102,
            PulseRate = 112, TemperatureCelsius = 38.6m, Consciousness = Consciousness.Alert,
        };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(7, result.Total);
        Assert.Equal(RiskLevel.High, result.Risk);
    }

    [Fact]
    public void CopdOnScale2_ScoresFive()
    {
        // RR 22 → 2, SpO2 90 (Scale 2) → 0, oxygen → 2, SBP 132 → 0, HR 96 → 1, 37.2 °C → 0, Alert → 0
        var input = new News2Input
        {
            RespirationRate = 22, SpO2 = 90, SpO2Scale = SpO2Scale.Scale2,
            Scale2Authorisation = TestInputs.ValidScale2Decision(), OnSupplementalOxygen = true,
            SystolicBp = 132, PulseRate = 96, TemperatureCelsius = 37.2m, Consciousness = Consciousness.Alert,
        };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(5, result.Total);
        Assert.Equal(RiskLevel.Medium, result.Risk);
    }

    [Fact]
    public void HiddenBleed_ScoresNine()
    {
        // RR 21 → 2, SpO2 96 → 0, air → 0, SBP 96 → 2, HR 124 → 2, 36.2 °C → 0, confusion (onset unknown) → 3
        var input = new News2Input
        {
            RespirationRate = 21, SpO2 = 96, OnSupplementalOxygen = false, SystolicBp = 96,
            PulseRate = 124, TemperatureCelsius = 36.2m, Consciousness = Consciousness.Confusion,
        };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(9, result.Total);
        Assert.Equal(RiskLevel.High, result.Risk);
    }
}
