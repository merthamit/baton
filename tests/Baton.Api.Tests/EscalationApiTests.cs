using System.Text.Json;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// The chain state served for the card, from FHIR records. Bed 02: called 23:19, arrived and assessed 23:40,
// plan 00:05, critical care called 00:20 (score High at 00:15), simulated arrival 00:30.
public class EscalationApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private HttpClient ClientWith(params (string Key, string Value)[] settings) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
        foreach (var (key, value) in settings)
        {
            b.UseSetting(key, value);
        }
    }).CreateClient();

    private static DateTimeOffset At(double hour) => ShiftSimulator.AtMinute(Start, hour);

    private async Task<JsonElement> Chain(double hour, string bed = "02", HttpClient? client = null)
    {
        using var doc = JsonDocument.Parse(await (client ?? ClientWith()).GetStringAsync($"/api/ward?at={Uri.EscapeDataString(At(hour).ToString("O"))}"));
        return doc.RootElement.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("bed").GetString() == bed).GetProperty("escalation").Clone();
    }

    [Fact]
    public async Task Bed02_AtMidnight_Assessed()
    {
        var chain = await Chain(16); // 00:00

        Assert.Equal("Assessed", chain.GetProperty("step").GetString());
        Assert.Equal(JsonValueKind.Null, chain.GetProperty("openCall").ValueKind);
        Assert.Equal("On-call doctor: suspected sepsis", chain.GetProperty("calls")[0].GetProperty("assessment").GetString());
    }

    [Fact]
    public async Task Bed02_At0016_SuggestsCriticalCare()
    {
        var chain = await Chain(16.27); // 00:16

        Assert.Equal("CriticalCare", chain.GetProperty("suggestedNextTier").GetString());
        Assert.False(chain.GetProperty("targetPassed").GetBoolean());
    }

    [Fact]
    public async Task Bed02_At0035_CriticalCareArrivedSimulated()
    {
        var chain = await Chain(16.5833); // 00:35

        var cct = chain.GetProperty("calls")[1];
        Assert.Equal("CriticalCare", cct.GetProperty("tier").GetString());
        Assert.True(cct.GetProperty("arrivalSimulated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, chain.GetProperty("suggestedNextTier").ValueKind);
    }

    [Theory]
    [InlineData("8", "0")]
    [InlineData("30", "0")]
    [InlineData("8", "3")]
    [InlineData("30", "3")]
    public async Task NoBed_EverShowsTargetPassedForever(string beds, string delay)
    {
        var client = ClientWith(("Baton:Beds", beds), ("Baton:TreatmentDelayHours", delay));
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/ward?at={Uri.EscapeDataString(At(24).ToString("O"))}"));

        Assert.All(doc.RootElement.GetProperty("beds").EnumerateArray(), b =>
            Assert.False(b.GetProperty("escalation").GetProperty("targetPassed").GetBoolean(), b.GetProperty("bed").GetString()));
    }

    [Fact]
    public async Task CustomTargetMinutes_Configurable()
    {
        var chain = await Chain(15.5, client: ClientWith(("Baton:MediumTargetMinutes", "45")));

        Assert.Equal(At(15.25).AddMinutes(4 + 45), chain.GetProperty("openCall").GetProperty("targetAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Patient_HasChainAndTimelineRecords()
    {
        using var doc = JsonDocument.Parse(await ClientWith().GetStringAsync($"/api/patients/synthetic-02?at={Uri.EscapeDataString(At(17).ToString("O"))}"));
        var records = doc.RootElement.GetProperty("escalationRecords").EnumerateArray().ToList();

        Assert.Equal(At(16.0833).ToString("HH:mm"), doc.RootElement.GetProperty("escalation").GetProperty("calls")[0].GetProperty("plannedAt").GetDateTimeOffset().ToString("HH:mm"));
        Assert.Equal(["ConcernRaised", "Called", "Arrived", "Assessed", "Planned", "Called", "Arrived"], records.Select(r => r.GetProperty("kind").GetString()));
        Assert.True(records[^1].GetProperty("simulated").GetBoolean());
        Assert.Equal("CriticalCare", records[5].GetProperty("tier").GetString());
    }
}
