using Baton.Core.News2;

namespace Baton.Scenarios.Tests;

// Decision 2B: late treatment makes the patient worse; timely treatment reproduces the card.
public class DelayTests
{
    private static SimulatedPatient Run(double delay) =>
        ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet with { TreatmentDelayHours = delay });

    private static int Peak(SimulatedPatient p) => p.Measurements.Max(m => m.Score.Total!.Value);

    [Fact]
    public void NoDelay_IsExactlyTheCard()
    {
        var timely = ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet);
        var zero = Run(0);

        Assert.Equal(timely.Measurements.Select(m => (m.Hour, m.Vitals)), zero.Measurements.Select(m => (m.Hour, m.Vitals)));
    }

    [Fact]
    public void ThreeHoursLate_PeaksHigherAndBecomesConfused()
    {
        var timely = Run(0);
        var late = Run(3);

        Assert.True(Peak(late) > Peak(timely), $"late peak {Peak(late)} should exceed timely peak {Peak(timely)}");
        Assert.Contains(late.Measurements, m => m.Vitals.Consciousness == Consciousness.Confusion);
        Assert.DoesNotContain(timely.Measurements, m => m.Vitals.Consciousness == Consciousness.Confusion);
    }

    [Fact]
    public void OneHourLate_IsWorseButNotConfused()
    {
        var late = Run(1);

        Assert.True(Peak(late) > Peak(Run(0)));
        Assert.DoesNotContain(late.Measurements, m => m.Vitals.Consciousness == Consciousness.Confusion);
    }

    [Fact]
    public void ThreeHoursLate_RecoversLater()
    {
        var timely = Run(0);
        var late = Run(3);

        double FirstLowAfterPeak(SimulatedPatient p)
        {
            var peakHour = p.Measurements.MaxBy(m => m.Score.Total)!.Hour;
            return p.Measurements.First(m => m.Hour > peakHour && m.Score.Total <= 4).Hour;
        }

        Assert.True(FirstLowAfterPeak(late) > FirstLowAfterPeak(timely));
    }

    [Fact]
    public void DoctorAndTreatmentEvents_MoveWithTheDelay()
    {
        var timely = Run(0).Events;
        var late = Run(3).Events;

        double Hour(IReadOnlyList<ScenarioEvent> e, ScenarioEventKind k) => e.Single(x => x.Kind == k).Hour;
        Assert.Equal(Hour(timely, ScenarioEventKind.DoctorArrived) + 3, Hour(late, ScenarioEventKind.DoctorArrived), 3);
        Assert.Equal(Hour(timely, ScenarioEventKind.TreatmentStarted) + 3, Hour(late, ScenarioEventKind.TreatmentStarted), 3);
        Assert.Equal(Hour(timely, ScenarioEventKind.Concern), Hour(late, ScenarioEventKind.Concern), 3);
    }

    [Fact]
    public void CardWithoutDelayRule_IgnoresTheDelay()
    {
        var a = ShiftSimulator.Run(ScenarioCards.Stable, CardFidelityTests.Quiet);
        var b = ShiftSimulator.Run(ScenarioCards.Stable, CardFidelityTests.Quiet with { TreatmentDelayHours = 5 });

        Assert.Equal(a.Measurements.Select(m => m.Vitals), b.Measurements.Select(m => m.Vitals));
    }
}
