using Baton.Api.Shift;
using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Scenarios;

namespace Baton.Api.Tests;

// A visitor's private shift: a running ward clock and the nurse's and doctor's entries.
public class ShiftStoreTests
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset Real = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Real;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly VitalSet Normal = new(16, 97, 0, 125, 75, 36.8m, Consciousness.Alert);

    private static (WardShifts Shifts, Clock Clock) Make()
    {
        var clock = new Clock();
        return (new WardShifts(clock, new BatonOptionsSnapshot(ShiftStart, 42)), clock);
    }

    private static DateTimeOffset Ward(int hour, int minute = 0) => ShiftStart.AddHours(hour).AddMinutes(minute);

    [Fact]
    public void Clock_RunsAtTheChosenSpeed()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 10)!;

        clock.Now = Real.AddMinutes(3);

        Assert.Equal(Ward(14, 30), shifts.Now(shift));
    }

    [Fact]
    public void SpeedChange_KeepsTheWardTime_AndChangesTheRate()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 10)!;
        clock.Now = Real.AddMinutes(3); // ward 14:30

        shifts.SetSpeed(shift, 60);
        Assert.Equal(Ward(14, 30), shifts.Now(shift));

        clock.Now = Real.AddMinutes(4); // one more real minute at 60×
        Assert.Equal(Ward(15, 30), shifts.Now(shift));
    }

    [Fact]
    public void Clock_StopsAtTheShiftEnd()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(23), 60)!;

        clock.Now = Real.AddHours(5);

        Assert.Equal(ShiftStart.AddHours(ShiftSimulator.ShiftHours), shifts.Now(shift));
        Assert.Equal("shift-over", shifts.Observe(shift, "synthetic-01", Normal, null)?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(1000)]
    public void UnknownSpeed_IsRefused(int speed) =>
        Assert.Null(Make().Shifts.Start(Ward(14), speed));

    [Fact]
    public void Start_OutsideTheShift_IsRefused() =>
        Assert.Null(Make().Shifts.Start(ShiftStart.AddHours(-1), 10));

    [Fact]
    public void Observation_IsStampedWithWardNow()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 10)!;
        clock.Now = Real.AddMinutes(1);

        Assert.Null(shifts.Observe(shift, "synthetic-02", Normal, null));

        var entry = Assert.Single(shift.Observations);
        Assert.Equal(Ward(14, 10), entry.At);
        Assert.Equal(Ward(14, 10), entry.RecordedAt);
        Assert.Equal("synthetic-02", entry.PatientId);
    }

    [Fact]
    public void LateEntry_KeepsTakenAndRecordedTimesApart()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 10)!;
        clock.Now = Real.AddMinutes(3); // 14:30

        Assert.Null(shifts.Observe(shift, "synthetic-02", Normal, Ward(14, 15)));

        var entry = Assert.Single(shift.Observations);
        Assert.Equal(Ward(14, 15), entry.At);
        Assert.Equal(Ward(14, 30), entry.RecordedAt);
    }

    [Fact]
    public void TakenTime_InTheFutureOrBeforeTheStart_IsRefused()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 10)!;
        clock.Now = Real.AddMinutes(3);

        Assert.Equal("taken-time", shifts.Observe(shift, "synthetic-02", Normal, Ward(14, 31))?.Code);
        Assert.Equal("taken-time", shifts.Observe(shift, "synthetic-02", Normal, Ward(13, 59))?.Code);
    }

    [Fact]
    public void ValueOutOfRange_NamesTheField()
    {
        var (shifts, _) = Make();
        var shift = shifts.Start(Ward(14), 10)!;

        var refusal = shifts.Observe(shift, "synthetic-02", Normal with { SpO2 = 120 }, null);

        Assert.Equal("out-of-range", refusal?.Code);
        Assert.Equal("spO2", refusal?.Field);
    }

    [Fact]
    public void Chain_Call_Acknowledge_Arrive_Assess_Plan()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 10)!;
        void Act(ShiftActionKind kind, int realMinute, string? text = null, EscalationTier? tier = null)
        {
            clock.Now = Real.AddMinutes(realMinute);
            Assert.Null(shifts.Act(shift, "synthetic-02", kind, text, tier));
        }

        Act(ShiftActionKind.Call, 1, tier: EscalationTier.OnCallDoctor);
        Act(ShiftActionKind.Acknowledge, 2);
        Act(ShiftActionKind.Arrive, 3);
        Act(ShiftActionKind.Assess, 4, "Suspected sepsis");
        Act(ShiftActionKind.Plan, 5, "Fluids, antibiotics");

        var records = shift.RecordsOf("synthetic-02");
        var call = Assert.IsType<Called>(records[0]);
        Assert.Equal(Ward(14, 10), call.At);
        Assert.Equal(TryCourse.You, call.By);
        Assert.Equal(call.CallId, Assert.IsType<Acknowledged>(records[1]).CallId);
        Assert.Equal(ScenarioEscalation.OnCallDoctor, records[1].By);
        Assert.False(Assert.IsType<Arrived>(records[2]).Simulated);
        Assert.Equal("Suspected sepsis", Assert.IsType<Assessed>(records[3]).Text);
        Assert.Equal(Ward(14, 50), Assert.IsType<Planned>(records[4]).At);
    }

    [Fact]
    public void DoctorSteps_OutOfOrder_AreRefused()
    {
        var (shifts, _) = Make();
        var shift = shifts.Start(Ward(14), 10)!;

        Assert.Equal("no-open-call", shifts.Act(shift, "synthetic-02", ShiftActionKind.Acknowledge, null, null)?.Code);
        Assert.Equal("no-open-call", shifts.Act(shift, "synthetic-02", ShiftActionKind.Arrive, null, null)?.Code);
        Assert.Equal("no-arrival", shifts.Act(shift, "synthetic-02", ShiftActionKind.Assess, "x", null)?.Code);

        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Call, null, EscalationTier.OnCallDoctor));
        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Acknowledge, null, null));
        Assert.Equal("already-acknowledged", shifts.Act(shift, "synthetic-02", ShiftActionKind.Acknowledge, null, null)?.Code);
        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Arrive, null, null));
        Assert.Equal("assessment-first", shifts.Act(shift, "synthetic-02", ShiftActionKind.Plan, "x", null)?.Code);
    }

    [Fact]
    public void ArrivalWithoutAcknowledgement_AcknowledgesToo()
    {
        var (shifts, _) = Make();
        var shift = shifts.Start(Ward(14), 10)!;
        shifts.Act(shift, "synthetic-02", ShiftActionKind.Call, null, EscalationTier.SeniorDoctor);

        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Arrive, null, null));

        Assert.Contains(shift.RecordsOf("synthetic-02"), r => r is Acknowledged a && a.By == ScenarioEscalation.SeniorDoctor);
    }

    [Fact]
    public void NotDone_IsRecordedWithItsReason()
    {
        var (shifts, _) = Make();
        var shift = shifts.Start(Ward(14), 10)!;

        Assert.Null(shifts.NotDone(shift, "synthetic-03", NotDoneReason.PatientDeclined, "Wants to sleep"));

        var record = Assert.IsType<ObservationNotDone>(Assert.Single(shift.RecordsOf("synthetic-03")));
        Assert.Equal(NotDoneReason.PatientDeclined, record.Reason);
        Assert.Equal(Ward(14), record.At);
    }

    [Fact]
    public void LongText_IsRefused()
    {
        var (shifts, _) = Make();
        var shift = shifts.Start(Ward(14), 10)!;

        Assert.Equal("note-too-long", shifts.Act(shift, "synthetic-02", ShiftActionKind.Concern, new string('x', 501), null)?.Code);
    }

    [Fact]
    public void ExpiredShift_IsGone()
    {
        var (shifts, clock) = Make();
        var shift = shifts.Start(Ward(14), 1)!;

        clock.Now = Real + WardShifts.Lifetime + TimeSpan.FromMinutes(1);

        Assert.Null(shifts.Get(shift.Id));
    }
}
