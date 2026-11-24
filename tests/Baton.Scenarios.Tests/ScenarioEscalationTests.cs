using Baton.Core.Escalation;
using Baton.Core.News2;

namespace Baton.Scenarios.Tests;

public class ScenarioEscalationTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;
    private static readonly IReadOnlyList<WardBed> WardBeds = Ward.Generate(Start, 42);

    private static DateTimeOffset Clock(int hour, int minute) =>
        new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(3)).AddHours(hour).AddMinutes(minute);

    [Fact]
    public void Bed02_RecordsMatchTheStory()
    {
        var records = ScenarioEscalation.From(WardBeds[1], Start);

        Assert.Collection(
            records,
            r => Assert.Equal((typeof(ConcernRaised), Clock(20, 15)), (r.GetType(), r.At)),
            r => Assert.Equal((typeof(Called), Clock(23, 19)), (r.GetType(), r.At)),
            r => Assert.Equal((typeof(Arrived), Clock(23, 40)), (r.GetType(), r.At)),
            r => Assert.Equal((typeof(Assessed), Clock(23, 40)), (r.GetType(), r.At)),
            r => Assert.Equal((typeof(Planned), Clock(24, 5)), (r.GetType(), r.At)),
            r => Assert.Equal((typeof(Called), Clock(24, 20)), (r.GetType(), r.At)),
            r => Assert.Equal((typeof(Arrived), Clock(24, 30)), (r.GetType(), r.At)));

        Assert.Equal(EscalationTier.OnCallDoctor, records.OfType<Called>().First().Tier);
        Assert.Equal(EscalationTier.CriticalCare, records.OfType<Called>().Last().Tier);
        Assert.False(records.OfType<Arrived>().First().Simulated);
        Assert.True(records.OfType<Arrived>().Last().Simulated);
        Assert.Equal("On-call doctor: suspected sepsis", records.OfType<Assessed>().Single().Text);
        Assert.All(records.Where(r => r is ConcernRaised or Called), r => Assert.Equal(ScenarioEscalation.ScenarioNurse, r.By));
    }

    [Fact]
    public void StableBed_NoRecords()
    {
        Assert.Empty(ScenarioEscalation.From(WardBeds[0], Start));
    }

    [Fact]
    public void ArrivalWithoutCall_Skipped()
    {
        var card = new ScenarioCard(
            "lonely", "Arrival without call", "test", SpO2Scale.Scale1,
            [new Keyframe(0, new VitalSet(16, 97, 0, 120, 70, 37.0m, Consciousness.Alert))],
            [new ScenarioEvent(2, ScenarioEventKind.DoctorArrived, "came anyway")],
            null);
        var bed = new WardBed("99", "synthetic-99", "Test", 50, "test", ShiftSimulator.Run(card, CardFidelityTests.Quiet));

        Assert.Empty(ScenarioEscalation.From(bed, Start));
    }

    [Fact]
    public void CallIds_AreUniquePerCall()
    {
        var calls = WardBeds.SelectMany(b => ScenarioEscalation.From(b, Start).OfType<Called>()).ToList();

        Assert.Equal(calls.Count, calls.Select(c => c.CallId).Distinct().Count());
        Assert.NotEmpty(calls);
    }
}
