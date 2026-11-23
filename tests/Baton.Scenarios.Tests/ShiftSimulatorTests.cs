using Baton.Core.News2;

namespace Baton.Scenarios.Tests;

public class ShiftSimulatorTests
{
    private static readonly SimulationOptions Quiet = CardFidelityTests.Quiet;

    private static ScenarioCard Flat(int pulse, params ScenarioEvent[] events) => new(
        "flat", "Flat", "test", SpO2Scale.Scale1,
        [new Keyframe(0, new VitalSet(16, 97, 0, 120, pulse, 37.0m, Consciousness.Alert))],
        events, null);

    [Fact]
    public void ScoreZero_NextMeasurementIsTwelveHoursLater()
    {
        Assert.Equal([0, 12], ShiftSimulator.Run(Flat(70), Quiet).Measurements.Select(m => m.Hour));
    }

    [Fact]
    public void ScoreOneToFour_NextMeasurementIsFourHoursLater()
    {
        // pulse 95 scores 1
        Assert.Equal([0, 4, 8, 12, 16, 20], ShiftSimulator.Run(Flat(95), Quiet).Measurements.Select(m => m.Hour));
    }

    [Fact]
    public void SingleParameterScoring3_IsMeasuredHourly()
    {
        // pulse 135 scores 3
        Assert.Equal(24, ShiftSimulator.Run(Flat(135), Quiet).Measurements.Count);
    }

    [Fact]
    public void ConcernFlag_BringsTheNextMeasurementForwardThenTwoHourly()
    {
        var p = ShiftSimulator.Run(Flat(70, new ScenarioEvent(3, ScenarioEventKind.Concern, "worried")), Quiet);

        Assert.Equal([0, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22], p.Measurements.Select(m => m.Hour));
    }

    [Fact]
    public void NoMeasurementAtOrAfterTheEndOfTheShift()
    {
        Assert.All(ShiftSimulator.Run(Flat(135), Quiet).Measurements, m => Assert.True(m.Hour < 24));
    }

    [Fact]
    public void MeasurementTimes_AreShiftStartPlusHours()
    {
        var m = ShiftSimulator.Run(ScenarioCards.Sepsis, Quiet).Measurements.Single(x => x.Hour == 13.25);

        Assert.Equal(Quiet.ShiftStart.AddHours(13.25), m.At);
    }

    [Fact]
    public void Sepsis_DerivesAlertDoctorCallAndCriticalCareOnce()
    {
        var events = ShiftSimulator.Run(ScenarioCards.Sepsis, Quiet).Events;

        var alert = Assert.Single(events, e => e.Kind == ScenarioEventKind.Alert);
        Assert.Equal(15.25, alert.Hour, 3);
        var call = Assert.Single(events, e => e.Kind == ScenarioEventKind.DoctorCalled);
        Assert.Equal(15.25 + (4 / 60.0), call.Hour, 3);
        var critical = Assert.Single(events, e => e.Kind == ScenarioEventKind.CriticalCareCalled);
        Assert.Equal(16.25 + (5 / 60.0), critical.Hour, 3);
        Assert.Contains(events, e => e.Kind == ScenarioEventKind.Concern);
        Assert.Equal(events.OrderBy(e => e.Hour), events);
    }
}
