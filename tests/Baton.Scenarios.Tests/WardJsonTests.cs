using System.Text.Json;

namespace Baton.Scenarios.Tests;

public class WardJsonTests
{
    [Fact]
    public void Json_HasEveryBedMeasurementAndEvent()
    {
        var ward = Ward.Generate(CardFidelityTests.Quiet.ShiftStart, 42);

        using var doc = JsonDocument.Parse(WardJson.Serialize(ward, CardFidelityTests.Quiet.ShiftStart, seed: 42, treatmentDelayHours: 0));
        var beds = doc.RootElement.GetProperty("beds");

        Assert.Equal(8, beds.GetArrayLength());
        var y02 = beds.EnumerateArray().Single(b => b.GetProperty("bed").GetString() == "02");
        Assert.Equal(ward[1].Patient.Measurements.Count, y02.GetProperty("measurements").GetArrayLength());
        Assert.Equal(ward[1].Patient.Events.Count, y02.GetProperty("events").GetArrayLength());

        var first = y02.GetProperty("measurements")[0];
        Assert.Equal(ward[1].Patient.Measurements[0].Score.Total, first.GetProperty("news2").GetProperty("total").GetInt32());
        Assert.Equal(7, first.GetProperty("news2").GetProperty("subScores").EnumerateObject().Count());
        Assert.Equal("Alert", first.GetProperty("vitals").GetProperty("consciousness").GetString());
    }

    [Fact]
    public void Json_IsTheSameForTheSameSeed()
    {
        var start = CardFidelityTests.Quiet.ShiftStart;

        Assert.Equal(
            WardJson.Serialize(Ward.Generate(start, 5), start, 5, 0),
            WardJson.Serialize(Ward.Generate(start, 5), start, 5, 0));
    }
}
