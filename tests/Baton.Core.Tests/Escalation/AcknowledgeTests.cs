using Baton.Core.Escalation;
using Baton.Core.News2;

namespace Baton.Core.Tests.Escalation;

// A real ward closes a call only when the clinician acknowledges it: called → acknowledged → arrived.
public class AcknowledgeTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(3));

    private static DateTimeOffset T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

    private const string Nurse = "Practitioner/synthetic-nurse";
    private const string Doctor = "Practitioner/synthetic-oncall";

    private static readonly ScoreMark[] Scores = [new(T(2, 0), RiskLevel.Medium)];

    [Fact]
    public void AcknowledgedCall_IsAcknowledgedStep_WithItsTime()
    {
        EscalationRecord[] records =
        [
            new Called(T(2, 5), Nurse, "c1", EscalationTier.OnCallDoctor),
            new Acknowledged(T(2, 9), Doctor, "c1"),
        ];

        var state = EscalationChain.At(records, Scores, T(2, 20));

        Assert.Equal(EscalationStep.Acknowledged, state.Step);
        Assert.Equal(T(2, 9), state.Calls[0].AcknowledgedAt);
        Assert.Equal(Doctor, state.Calls[0].AcknowledgedBy);
        Assert.Same(state.Calls[0], state.OpenCall); // still waiting for the arrival
    }

    [Fact]
    public void AcknowledgementOfAnotherCall_DoesNotCount()
    {
        EscalationRecord[] records =
        [
            new Called(T(2, 5), Nurse, "c1", EscalationTier.OnCallDoctor),
            new Acknowledged(T(2, 9), Doctor, "other"),
        ];

        var state = EscalationChain.At(records, Scores, T(2, 20));

        Assert.Equal(EscalationStep.Called, state.Step);
        Assert.Null(state.Calls[0].AcknowledgedAt);
    }

    [Fact]
    public void AcknowledgementAfterTheMoment_DoesNotCount()
    {
        EscalationRecord[] records =
        [
            new Called(T(2, 5), Nurse, "c1", EscalationTier.OnCallDoctor),
            new Acknowledged(T(2, 30), Doctor, "c1"),
        ];

        Assert.Equal(EscalationStep.Called, EscalationChain.At(records, Scores, T(2, 20)).Step);
    }

    [Fact]
    public void ArrivalAfterAcknowledgement_IsArrivedStep()
    {
        EscalationRecord[] records =
        [
            new Called(T(2, 5), Nurse, "c1", EscalationTier.OnCallDoctor),
            new Acknowledged(T(2, 9), Doctor, "c1"),
            new Arrived(T(2, 30), Doctor, "c1", Simulated: false),
        ];

        var state = EscalationChain.At(records, Scores, T(2, 40));

        Assert.Equal(EscalationStep.Arrived, state.Step);
        Assert.Equal(T(2, 9), state.Calls[0].AcknowledgedAt);
        Assert.Null(state.OpenCall);
    }

    // Review I2: once a later (more senior) call is answered, an earlier unanswered call no longer waits.
    [Fact]
    public void EarlierUnansweredCall_IsNotOpen_OnceALaterCallIsAnswered()
    {
        EscalationRecord[] records =
        [
            new Called(T(2, 5), Nurse, "c1", EscalationTier.OnCallDoctor),
            new Called(T(3, 30), Nurse, "c2", EscalationTier.SeniorDoctor),
            new Arrived(T(3, 40), "Practitioner/synthetic-senior", "c2", Simulated: false),
        ];

        var state = EscalationChain.At(records, Scores, T(4, 0));

        Assert.Null(state.OpenCall);
        Assert.False(state.TargetPassed);
        Assert.Null(state.MinutesWaited);
    }

    [Fact]
    public void ObservationNotDone_DoesNotChangeTheChain()
    {
        EscalationRecord[] records = [new ObservationNotDone(T(2, 10), Nurse, NotDoneReason.PatientAsleep, null)];

        var state = EscalationChain.At(records, Scores, T(2, 20));

        Assert.Equal(EscalationStep.None, state.Step);
        Assert.Empty(state.Calls);
    }
}
