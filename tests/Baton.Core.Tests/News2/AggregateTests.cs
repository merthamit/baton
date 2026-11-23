using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

public class AggregateTests
{
    [Fact]
    public void Calculate_AllNormal_IsZeroWithRoutineMonitoring()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal());

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.Total);
        Assert.Equal(RiskLevel.Low, result.Risk);
        Assert.Equal(ClinicalResponse.RoutineMonitoring, result.Response);
        Assert.Equal("At least 12-hourly", result.MinimumMonitoringFrequency);
        Assert.False(result.HasSingleParameterScoreOf3);
    }

    [Fact]
    public void Calculate_TotalOneToFour_IsWardBasedResponse()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { SpO2 = 95 });

        Assert.Equal(1, result.Total);
        Assert.Equal(RiskLevel.Low, result.Risk);
        Assert.Equal(ClinicalResponse.WardBasedResponse, result.Response);
        Assert.Equal("At least 4–6-hourly", result.MinimumMonitoringFrequency);
    }

    [Fact]
    public void Calculate_SingleParameterScoring3_IsLowMediumUrgentWardBasedResponse()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { PulseRate = 131 });

        Assert.Equal(3, result.Total);
        Assert.Equal(RiskLevel.LowMedium, result.Risk);
        Assert.Equal(ClinicalResponse.UrgentWardBasedResponse, result.Response);
        Assert.Equal("At least hourly", result.MinimumMonitoringFrequency);
        Assert.True(result.HasSingleParameterScoreOf3);
        Assert.Equal([News2Parameter.PulseRate], result.ParametersScoring3);
    }

    [Fact]
    public void Calculate_TotalFive_IsMediumUrgentResponse()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { RespirationRate = 21, PulseRate = 111, SpO2 = 95 });

        Assert.Equal(5, result.Total);
        Assert.Equal(RiskLevel.Medium, result.Risk);
        Assert.Equal(ClinicalResponse.UrgentResponse, result.Response);
        Assert.Equal("At least hourly", result.MinimumMonitoringFrequency);
    }

    [Fact]
    public void Calculate_TotalSeven_IsHighEmergencyResponse()
    {
        var input = TestInputs.Normal() with { RespirationRate = 25, PulseRate = 111, SystolicBp = 101, SpO2 = 95 };

        var result = News2Calculator.Calculate(input);

        Assert.Equal(7, result.Total);
        Assert.Equal(RiskLevel.High, result.Risk);
        Assert.Equal(ClinicalResponse.EmergencyResponse, result.Response);
        Assert.Equal("Continuous monitoring", result.MinimumMonitoringFrequency);
    }

    [Fact]
    public void Calculate_IncompleteWithParameterScoring3_StillReportsTheRedParameter()
    {
        var result = News2Calculator.Calculate(TestInputs.Normal() with { TemperatureCelsius = null, PulseRate = 135 });

        Assert.False(result.IsComplete);
        Assert.Null(result.Total);
        Assert.Null(result.Risk);
        Assert.Null(result.Response);
        Assert.Null(result.MinimumMonitoringFrequency);
        Assert.True(result.HasSingleParameterScoreOf3);
        Assert.Equal([News2Parameter.PulseRate], result.ParametersScoring3);
    }
}
