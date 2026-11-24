using Baton.Core.News2;

namespace Baton.Scenarios.Tests;

// Shift mode's "Measure": the patient's true vitals at a moment, driven by real arrivals. Bed 02 (sepsis): timely
// treatment 00:05 after the on-call arrival 23:40 (card gap 25 min).
public class TrueVitalsTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;

    private static DateTimeOffset At(double hour) => ShiftSimulator.AtMinute(Start, hour);

    private static int Score(VitalSet v, SpO2Scale scale) =>
        News2Calculator.Calculate(v.ToNews2Input(scale, null)).Total ?? 0;

    [Fact]
    public void NoArrival_KeepsWorsening_PastTheTimelyCourse()
    {
        var timely = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [At(15 + (40 / 60.0))], At(19));
        var nobody = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [], At(19));

        Assert.True(Score(nobody.Vitals, nobody.Scale) > Score(timely.Vitals, timely.Scale), $"{Score(nobody.Vitals, nobody.Scale)} vs {Score(timely.Vitals, timely.Scale)}");
    }

    [Fact]
    public void LateArrival_IsWorseThanTimely_AtTheSameMoment()
    {
        var timely = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [At(15 + (40 / 60.0))], At(18));
        var late = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [At(17.5)], At(18));

        Assert.True(Score(late.Vitals, late.Scale) > Score(timely.Vitals, timely.Scale));
    }

    [Fact]
    public void BeforeTreatment_MatchesTheCard_WhateverTheArrival()
    {
        var a = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [], At(14.5));
        var b = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [At(15)], At(14.5));

        Assert.Equal(Score(a.Vitals, a.Scale), Score(b.Vitals, b.Scale));
    }

    [Fact]
    public void Noise_NeverChangesTheScore()
    {
        for (var minute = 0; minute < 120; minute += 7)
        {
            var at = At(14).AddMinutes(minute);
            var noisy = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [], at);
            var quiet = TryCourse.TrueVitalsAt(Start, 42, 1, At(14), [], at, noiseLevel: 0);

            Assert.Equal(Score(quiet.Vitals, quiet.Scale), Score(noisy.Vitals, noisy.Scale));
        }
    }
}
