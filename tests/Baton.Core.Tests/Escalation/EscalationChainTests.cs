using Baton.Core.Escalation;
using Baton.Core.News2;

namespace Baton.Core.Tests.Escalation;

// Bed 02's night: concern 20:15, on-call doctor called 23:19 (score Medium at 23:15), arrived and assessed 23:40,
// plan 00:05, critical care called 00:20 (score High at 00:15), simulated arrival 00:30.
public class EscalationChainTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(3));

    private static DateTimeOffset T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

    private const string Nurse = "Practitioner/synthetic-nurse";

    private static readonly ScoreMark[] Scores =
    [
        new(T(20, 0), RiskLevel.Low),
        new(T(23, 15), RiskLevel.Medium),
        new(T(24, 15), RiskLevel.High),
        new(T(25, 15), RiskLevel.High),
    ];

    private static readonly EscalationRecord[] Story =
    [
        new ConcernRaised(T(20, 15), Nurse, "Restless, not eating"),
        new Called(T(23, 19), Nurse, "call-1", EscalationTier.OnCallDoctor),
        new Arrived(T(23, 40), "Practitioner/synthetic-oncall", "call-1", Simulated: false),
        new Assessed(T(23, 40), "Practitioner/synthetic-oncall", "call-1", "Suspected sepsis"),
        new Planned(T(24, 5), "Practitioner/synthetic-oncall", "call-1", "Cultures, fluids, antibiotics"),
        new Called(T(24, 20), Nurse, "call-2", EscalationTier.CriticalCare),
        new Arrived(T(24, 30), "Practitioner/synthetic-cct", "call-2", Simulated: true),
    ];

    [Fact]
    public void NoRecords_IsNone()
    {
        var state = EscalationChain.At([], Scores, T(23, 30));

        Assert.Equal(EscalationStep.None, state.Step);
        Assert.Null(state.OpenCall);
        Assert.Null(state.MinutesWaited);
        Assert.False(state.TargetPassed);
        Assert.Null(state.SuggestedNextTier);
    }

    [Fact]
    public void TargetFor_Medium60_High30_LowNone()
    {
        var options = new EscalationOptions();

        Assert.Equal(TimeSpan.FromMinutes(60), EscalationChain.TargetFor(RiskLevel.Medium, options));
        Assert.Equal(TimeSpan.FromMinutes(60), EscalationChain.TargetFor(RiskLevel.LowMedium, options));
        Assert.Equal(TimeSpan.FromMinutes(30), EscalationChain.TargetFor(RiskLevel.High, options));
        Assert.Null(EscalationChain.TargetFor(RiskLevel.Low, options));
        Assert.Null(EscalationChain.TargetFor(null, options));
    }

    [Fact]
    public void AfterCall_StepCalled_Waiting11_Target0019()
    {
        var state = EscalationChain.At(Story, Scores, T(23, 30));

        Assert.Equal(EscalationStep.Called, state.Step);
        Assert.Equal(T(20, 15), state.ConcernRaisedAt);
        Assert.Equal("call-1", state.OpenCall!.CallId);
        Assert.Equal(T(24, 19), state.OpenCall.TargetAt);
        Assert.Equal(11, state.MinutesWaited);
        Assert.False(state.TargetPassed);
        Assert.Null(state.SuggestedNextTier);
    }

    [Fact]
    public void AfterArrival_StepAssessed_NoOpenCall()
    {
        var state = EscalationChain.At(Story, Scores, T(23, 45));

        Assert.Equal(EscalationStep.Assessed, state.Step);
        Assert.Null(state.OpenCall);
        Assert.Null(state.MinutesWaited);
        Assert.Equal("Suspected sepsis", state.Calls.Single().Assessment);
    }

    [Fact]
    public void AfterPlan_StepPlanned()
    {
        var state = EscalationChain.At(Story, Scores, T(24, 10));

        Assert.Equal(EscalationStep.Planned, state.Step);
        Assert.Equal("Cultures, fluids, antibiotics", state.Calls.Single().Plan);
    }

    [Fact]
    public void RiskBecomesHigh_SuggestsCriticalCare()
    {
        var state = EscalationChain.At(Story, Scores, T(24, 16));

        Assert.Equal(EscalationTier.CriticalCare, state.SuggestedNextTier);
        Assert.False(state.TargetPassed);
    }

    [Fact]
    public void CriticalCareCalledAndArrived_NoFurtherSuggestion()
    {
        var state = EscalationChain.At(Story, Scores, T(24, 35));

        Assert.Equal(EscalationStep.Arrived, state.Step);
        Assert.Equal(2, state.Calls.Count);
        Assert.True(state.Calls[1].ArrivalSimulated);
        Assert.Null(state.SuggestedNextTier);
        Assert.False(state.TargetPassed);
    }

    [Fact]
    public void TargetPassed_SuggestsSeniorDoctor()
    {
        EscalationRecord[] records = [new Called(T(23, 19), Nurse, "call-1", EscalationTier.OnCallDoctor)];
        ScoreMark[] mediumOnly = [new(T(23, 15), RiskLevel.Medium), new(T(24, 15), RiskLevel.Medium)];

        var state = EscalationChain.At(records, mediumOnly, T(24, 25));

        Assert.True(state.TargetPassed);
        Assert.Equal(66, state.MinutesWaited);
        Assert.Equal(EscalationTier.SeniorDoctor, state.SuggestedNextTier);
    }

    [Fact]
    public void ScoreRisesWhileWaiting_TargetTightens()
    {
        EscalationRecord[] records = [new Called(T(23, 19), Nurse, "call-1", EscalationTier.OnCallDoctor)];
        ScoreMark[] rising = [new(T(23, 15), RiskLevel.Medium), new(T(23, 30), RiskLevel.High)];

        var state = EscalationChain.At(records, rising, T(23, 45));

        Assert.Equal(T(24, 0), state.OpenCall!.TargetAt);
    }

    [Fact]
    public void CallWithoutScore_NoTarget()
    {
        EscalationRecord[] records = [new Called(T(23, 19), Nurse, "call-1", EscalationTier.OnCallDoctor)];

        var state = EscalationChain.At(records, [], T(25, 0));

        Assert.Null(state.OpenCall!.TargetAt);
        Assert.False(state.TargetPassed);
        Assert.Null(state.SuggestedNextTier);
    }

    [Fact]
    public void ShuffledRecords_SameState()
    {
        var shuffled = Story.Reverse().ToArray();
        var scores = Scores.Reverse().ToArray();

        Assert.Equal(EscalationChain.At(Story, Scores, T(24, 35)), EscalationChain.At(shuffled, scores, T(24, 35)), new StateComparer());
    }

    [Fact]
    public void RecordsAfterMoment_Ignored()
    {
        var state = EscalationChain.At(Story, Scores, T(23, 0));

        Assert.Equal(EscalationStep.None, state.Step);
        Assert.Equal(T(20, 15), state.ConcernRaisedAt);
        Assert.Empty(state.Calls);
    }

    [Fact]
    public void PatientOrFamilyConcern_IsReported()
    {
        EscalationRecord[] records = [new PatientOrFamilyConcern(T(21, 0), "RelatedPerson/daughter", "She is not herself")];

        Assert.Equal(T(21, 0), EscalationChain.At(records, Scores, T(21, 5)).PatientConcernAt);
    }

    [Fact]
    public void CustomOptions_ChangeTargets()
    {
        var options = new EscalationOptions { MediumTargetMinutes = 45, HighTargetMinutes = 20 };

        var state = EscalationChain.At(Story, Scores, T(23, 30), options);

        Assert.Equal(T(24, 4), state.OpenCall!.TargetAt);
    }

    private sealed class StateComparer : IEqualityComparer<EscalationState>
    {
        public bool Equals(EscalationState? x, EscalationState? y) =>
            x is not null && y is not null
            && x.Step == y.Step && x.ConcernRaisedAt == y.ConcernRaisedAt && x.MinutesWaited == y.MinutesWaited
            && x.TargetPassed == y.TargetPassed && x.SuggestedNextTier == y.SuggestedNextTier
            && x.OpenCall == y.OpenCall && x.Calls.SequenceEqual(y.Calls);

        public int GetHashCode(EscalationState obj) => obj.Step.GetHashCode();
    }
}
