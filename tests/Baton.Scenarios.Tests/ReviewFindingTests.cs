using System.Text.Json;

namespace Baton.Scenarios.Tests;

// Regression tests for the week 3 whole-branch review.
public class ReviewFindingTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;

    [Fact]
    public void BedSeeds_AreFixedNumbersNotPerProcessHashes()
    {
        // HashCode.Combine is randomised per process; these values must be the same on every run.
        Assert.Equal(42_000_000, Ward.SeedFor(42, 0));
        Assert.Equal(42_000_007, Ward.SeedFor(42, 7));
        Assert.Equal(1_000_003, Ward.SeedFor(1, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void WithNoise_EveryBedKeepsTheCardsScores(double delay)
    {
        foreach (var seed in Enumerable.Range(1, 25))
        {
            var noisy = Ward.Generate(Start, seed, noiseLevel: 1, treatmentDelayHours: delay);
            var quiet = Ward.Generate(Start, seed, noiseLevel: 0, treatmentDelayHours: delay);
            foreach (var (n, q) in noisy.Zip(quiet))
            {
                Assert.Equal(
                    q.Patient.Measurements.Select(m => (m.Hour, m.Score.Total)),
                    n.Patient.Measurements.Select(m => (m.Hour, m.Score.Total)));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void DoctorNeverArrivesBeforeAnAlertOrConcern(double delay)
    {
        foreach (var bed in Ward.Generate(Start, 42, noiseLevel: 1, treatmentDelayHours: delay))
        {
            var arrived = bed.Patient.Events.FirstOrDefault(e => e.Kind == ScenarioEventKind.DoctorArrived);
            if (arrived is null)
            {
                continue;
            }

            Assert.Contains(bed.Patient.Events, e =>
                e.Kind is ScenarioEventKind.Alert or ScenarioEventKind.Concern && e.Hour <= arrived.Hour);
        }
    }

    [Fact]
    public void Recovering_AlertedPatientIsSeenByADoctor()
    {
        var p = ShiftSimulator.Run(ScenarioCards.Recovering, CardFidelityTests.Quiet);

        Assert.Contains(p.Events, e => e.Kind == ScenarioEventKind.DoctorArrived);
    }

    [Fact]
    public void LongDelay_DropsEventsAfterTheShift()
    {
        var p = ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet with { TreatmentDelayHours = 10 });

        Assert.All(p.Events, e => Assert.True(e.Hour < ShiftSimulator.ShiftHours, $"{e.Kind} at {e.Hour}"));
    }

    [Fact]
    public void Delay_MovesNotesThatDescribeTheTreatedPeriod()
    {
        var timely = ShiftSimulator.Run(ScenarioCards.CopdScale2, CardFidelityTests.Quiet).Events;
        var late = ShiftSimulator.Run(ScenarioCards.CopdScale2, CardFidelityTests.Quiet with { TreatmentDelayHours = 3 }).Events;

        var oxygenNote = (IReadOnlyList<ScenarioEvent> e) => e.Single(x => x.Text.StartsWith("Oxygen turned up", StringComparison.Ordinal)).Hour;
        Assert.Equal(oxygenNote(timely) + 3, oxygenNote(late), 3);
    }

    [Fact]
    public void NegativeDelay_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet with { TreatmentDelayHours = -1 }));

    [Fact]
    public void JsonTimes_AreWholeMinutes()
    {
        var json = WardJson.Serialize(Ward.Generate(Start, 42), Start, 42, 0);
        using var doc = JsonDocument.Parse(json);
        var y02 = doc.RootElement.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("bed").GetString() == "02");

        var doctor = y02.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "DoctorArrived");
        Assert.Equal(Start.AddMinutes((15 * 60) + 40), doctor.GetProperty("at").GetDateTimeOffset());
        Assert.DoesNotContain(".9999", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDelay_FollowsTheCardsTrueStateExactly()
    {
        var p = ShiftSimulator.Run(ScenarioCards.Sepsis, CardFidelityTests.Quiet);

        Assert.All(p.Measurements, m => Assert.Equal(TrueState.At(ScenarioCards.Sepsis, m.Hour), m.Vitals));
    }

    [Fact]
    public void Delay_IsContinuousAtTreatmentAndLateTreatment()
    {
        var card = ScenarioCards.Sepsis;
        var t = card.Delay!.TreatmentHour;
        foreach (var edge in new[] { t, t + 3 })
        {
            var before = Delay.Apply(card, edge - 0.0001, 3);
            var after = Delay.Apply(card, edge + 0.0001, 3);
            Assert.InRange(after.PulseRate - before.PulseRate, -1, 1);
            Assert.InRange(after.SystolicBp - before.SystolicBp, -1, 1);
        }
    }
}
