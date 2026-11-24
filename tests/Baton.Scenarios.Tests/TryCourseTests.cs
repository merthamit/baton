using Baton.Core.Escalation;

namespace Baton.Scenarios.Tests;

// Try mode: the user plays the nurse for one bed. Bed 02 (sepsis): in Watch the on-call doctor is called 23:19,
// arrives 23:40, treatment 00:05 (the card's timely treatment).
public class TryCourseTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;
    private static readonly IReadOnlyList<WardBed> Watch = Ward.Generate(Start, 42);

    private static DateTimeOffset At(double hour) => ShiftSimulator.AtMinute(Start, hour);

    private static TryResult Run(int bedIndex, double startHour, params TryAction[] actions) =>
        TryCourse.Run(Start, 42, bedIndex, 1, At(startHour), actions);

    private static int MaxAfter(WardBed bed, double hour) =>
        bed.Patient.Measurements.Where(m => m.Hour > hour).Max(m => m.Score.Total ?? 0);

    private static IEnumerable<(double, int?)> Scores(WardBed bed) => bed.Patient.Measurements.Select(m => (Math.Round(m.Hour, 6), m.Score.Total));

    private static TryAction Call(double hour, EscalationTier tier = EscalationTier.OnCallDoctor) =>
        new(TryActionKind.Call, At(hour), Tier: tier);

    [Fact]
    public void LateCall_WorsensTheCourse()
    {
        var result = Run(1, 14, Call(17)); // start 22:00, call 01:00

        Assert.True(MaxAfter(result.Bed, 17) > MaxAfter(Watch[1], 17), $"{MaxAfter(result.Bed, 17)} vs {MaxAfter(Watch[1], 17)}");
        Assert.Contains(result.Records, r => r is Called c && c.At == At(17) && c.By == TryCourse.You);
        Assert.Contains(result.Records, r => r is Arrived a && a.At == At(17).AddMinutes(21) && a.Simulated);
    }

    [Fact]
    public void EarlyCall_SameAsWatch()
    {
        var result = Run(1, 14, Call(15)); // call 23:00, before the scenario's 23:19

        Assert.Equal(Scores(Watch[1]), Scores(result.Bed));
    }

    [Fact]
    public void NoCall_TreatedAtShiftEnd_WorseThanALateCall()
    {
        var none = Run(1, 14);
        var late = Run(1, 14, Call(17));

        Assert.True(MaxAfter(none.Bed, 17) >= MaxAfter(late.Bed, 17));
        Assert.True(none.Bed.Patient.Measurements.Last().Score.Total > late.Bed.Patient.Measurements.Last().Score.Total);
    }

    [Fact]
    public void MidChainStart_KeepsOpenCallAndCourse()
    {
        var result = Run(1, 15.5); // start 23:30: the 23:19 call is open

        Assert.Contains(result.Records, r => r is Called c && c.At == At(15.25).AddMinutes(4));
        Assert.Contains(result.Records, r => r is Arrived a && a.At == At(15 + (40 / 60.0)) && a.Simulated);
        Assert.Equal(Scores(Watch[1]), Scores(result.Bed));
    }

    [Fact]
    public void UserConcern_DrivesSchedule()
    {
        var result = Run(0, 2, new TryAction(TryActionKind.Concern, At(7), "Looks unwell to me")); // stable bed, concern 15:00

        Assert.Contains(result.Bed.Patient.Measurements, m => Math.Abs(m.Hour - 8) < 1e-6); // measured within the hour
        Assert.Contains(result.Records, r => r is ConcernRaised c && c.Note == "Looks unwell to me" && c.By == TryCourse.You);
    }

    [Fact]
    public void StablePatient_CourseUnchanged()
    {
        var result = Run(0, 2, Call(7));

        Assert.Equal(Scores(Watch[0]), Scores(result.Bed));
    }

    [Fact]
    public void CriticalCareCall_Arrives10MinutesLater()
    {
        var result = Run(1, 14, Call(16.5, EscalationTier.CriticalCare));

        Assert.Contains(result.Records, r => r is Arrived a && a.At == At(16.5).AddMinutes(10) && a.Simulated);
    }

    [Fact]
    public void ScriptedAutoCalls_OffAfterStart()
    {
        var result = Run(1, 14);

        Assert.DoesNotContain(result.Records, r => r is Called);
        Assert.Contains(result.Records, r => r is ConcernRaised); // the 20:15 concern was before the start
        Assert.Contains(result.Bed.Patient.Events, e => e.Kind == ScenarioEventKind.Alert);
        Assert.DoesNotContain(result.Bed.Patient.Events, e => e.Kind is ScenarioEventKind.DoctorCalled or ScenarioEventKind.CriticalCareCalled);
    }

    [Fact]
    public void PatientOrFamilyConcern_RecordedFromTheFamily()
    {
        var result = Run(1, 14, new TryAction(TryActionKind.PatientOrFamilyConcern, At(14.5), "She is not herself"));

        Assert.Contains(result.Records, r => r is PatientOrFamilyConcern p && p.By == TryCourse.Family && p.Text == "She is not herself");
    }
}
