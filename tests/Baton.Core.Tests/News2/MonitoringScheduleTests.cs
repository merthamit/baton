using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

public class MonitoringScheduleTests
{
    private static readonly DateTimeOffset Eight = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private static DateTimeOffset At(int hour, int minute = 0) => Eight.AddHours(hour - 8).AddMinutes(minute);

    [Theory]
    [InlineData(ClinicalResponse.RoutineMonitoring, 12)]
    [InlineData(ClinicalResponse.WardBasedResponse, 4)]
    [InlineData(ClinicalResponse.UrgentWardBasedResponse, 1)]
    [InlineData(ClinicalResponse.UrgentResponse, 1)]
    [InlineData(ClinicalResponse.EmergencyResponse, 1)]
    public void IntervalFor_FollowsTheResponse(ClinicalResponse response, int hours)
    {
        Assert.Equal(TimeSpan.FromHours(hours), MonitoringSchedule.IntervalFor(response));
    }

    [Fact]
    public void IntervalFor_IncompleteScore_IsOneHour()
    {
        Assert.Equal(TimeSpan.FromHours(1), MonitoringSchedule.IntervalFor(null));
    }

    [Fact]
    public void NextDue_UsesInterval()
    {
        Assert.Equal(At(12), MonitoringSchedule.NextDue(At(8), ClinicalResponse.WardBasedResponse, []));
    }

    [Fact]
    public void NextDue_ActiveConcernCapsAtTwoHours()
    {
        Assert.Equal(At(10), MonitoringSchedule.NextDue(At(8), ClinicalResponse.RoutineMonitoring, [At(7, 30)]));
    }

    [Fact]
    public void NextDue_ConcernAfterLastObservation_DueOneHourLater()
    {
        Assert.Equal(At(10, 10), MonitoringSchedule.NextDue(At(8), ClinicalResponse.RoutineMonitoring, [At(9, 10)]));
    }

    [Fact]
    public void NextDue_LaterConcernBeyondDue_Ignored()
    {
        Assert.Equal(At(9), MonitoringSchedule.NextDue(At(8), ClinicalResponse.UrgentResponse, [At(8, 30)]));
    }
}
