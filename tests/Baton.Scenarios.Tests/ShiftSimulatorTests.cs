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

    // Characterisation: the measurement schedule of the default ward (noise 0) as it was before the schedule
    // rule moved to Baton.Core, plus bed 04's deliberately late set (decision 9A). Indexed by bed position.
    public static TheoryData<double, int, double[]> DefaultWardHours => new()
    {
        { 0, 0, [0, 12] },
        { 0, 1, [0, 12, 13.25, 15.25, 16.25, 17.25, 18.25, 19.25, 21.25, 23.25] },
        { 0, 2, [0, 12] },
        { 0, 3, [0, 12, 14.5, 15.5, 17.25, 19.25, 21.25, 23.25] }, // decision 9A: the 00:30 set is 45 min late
        { 0, 4, [0, 4, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 21, 23] },
        { 0, 5, [0, 1, 5, 9, 21] },
        { 0, 6, [0, 7.25, 9.25, 10.25, 11.25, 12.25, 13.25, 15.25, 17.25, 19.25, 21.25, 23.25] },
        { 0, 7, [0, 12] },
        { 3, 1, [0, 12, 13.25, 15.25, 16.25, 17.25, 18.25, 19.25, 20.25, 21.25, 22.25] },
        { 3, 3, [0, 12, 14.5, 15.5, 17.25, 18.25, 19.25, 20.25, 21.25, 22.25] }, // decision 9A
        { 3, 4, [0, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 19, 21, 23] },
        { 3, 6, [0, 7.25, 9.25, 10.25, 11.25, 12.25, 13.25, 14.25, 15.25, 16.25, 18.25, 20.25, 22.25] },
    };

    [Theory]
    [MemberData(nameof(DefaultWardHours))]
    public void DefaultWard_MeasurementHours_AreUnchanged(double delay, int bedIndex, double[] hours)
    {
        var ward = Ward.Generate(Quiet.ShiftStart, 42, noiseLevel: 0, treatmentDelayHours: delay);

        Assert.Equal(hours, ward[bedIndex].Patient.Measurements.Select(m => Math.Round(m.Hour, 6)));
    }

    [Fact]
    public void LateObservation_MovesThatSetAndNotesWhy()
    {
        // pulse 95 scores 1 → 4-hourly: 0, 4, 8, ... The set due at hour 8 is taken 45 minutes late.
        var options = Quiet with { LateObservation = new LateObservation(8, 45, "Team at another emergency") };

        var p = ShiftSimulator.Run(Flat(95), options);

        Assert.Equal([0, 4, 8.75, 12.75, 16.75, 20.75], p.Measurements.Select(m => Math.Round(m.Hour, 6)));
        var note = Assert.Single(p.Events, e => e.Kind == ScenarioEventKind.Note);
        Assert.Equal(8.75, note.Hour, 6);
        Assert.Contains("45 min late", note.Text, StringComparison.Ordinal);
        Assert.Contains("Team at another emergency", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LateObservation_ForAnHourThatIsNeverDue_ChangesNothing()
    {
        var options = Quiet with { LateObservation = new LateObservation(9, 45, "never due") };

        var p = ShiftSimulator.Run(Flat(95), options);

        Assert.Equal([0, 4, 8, 12, 16, 20], p.Measurements.Select(m => m.Hour));
        Assert.DoesNotContain(p.Events, e => e.Kind == ScenarioEventKind.Note);
    }

    [Fact]
    public void Ward_Bed04_SetDueAt0030_IsTaken45MinutesLate()
    {
        var bed04 = Ward.Generate(Quiet.ShiftStart, 42, noiseLevel: 0)[3];

        Assert.DoesNotContain(bed04.Patient.Measurements, m => Math.Abs(m.Hour - 16.5) < 1e-6);
        Assert.Contains(bed04.Patient.Measurements, m => Math.Abs(m.Hour - 17.25) < 1e-6);
        Assert.Contains(bed04.Patient.Events, e => e.Kind == ScenarioEventKind.Note && Math.Abs(e.Hour - 17.25) < 1e-6);
    }

    [Fact]
    public void DoctorAlreadyArrivedOnTheCard_NoAutomaticCallAfterwards()
    {
        // Pulse 135 scores 3 from the start (urgent); the card's doctor arrives at hour 0.5, before the first alert's call.
        var card = Flat(135, new ScenarioEvent(-1, ScenarioEventKind.DoctorArrived, "doctor already on the ward"));

        var p = ShiftSimulator.Run(card, Quiet);

        Assert.DoesNotContain(p.Events, e => e.Kind == ScenarioEventKind.DoctorCalled);
        Assert.Contains(p.Events, e => e.Kind == ScenarioEventKind.Alert);
    }
}
