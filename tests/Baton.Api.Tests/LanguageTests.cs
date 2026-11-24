using System.Text.Json;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// ?lang=tr gives the data in Turkish (FHIR translation extension, generator texts by their Turkish versions);
// without it, or with an unknown language, the original English stays.
public class LanguageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private HttpClient Client => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
    }).CreateClient();

    private static string At(double hour) => Uri.EscapeDataString(ShiftSimulator.AtMinute(Start, hour).ToString("O"));

    private async Task<JsonElement> Bed02(string lang)
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/ward?at={At(16)}{lang}"));
        return doc.RootElement.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("bed").GetString() == "02").Clone();
    }

    [Fact]
    public async Task Ward_InTurkish_TranslatesDiagnosisAndChain()
    {
        var bed = await Bed02("&lang=tr");

        Assert.Equal("Toplum kökenli pnömoni", bed.GetProperty("diagnosis").GetString());
        Assert.Equal("Huzursuz, yemek yemiyor, kendinde değil", bed.GetProperty("escalation").GetProperty("concernNote").GetString());
        Assert.Equal("Nöbetçi hekim: sepsis şüphesi", bed.GetProperty("escalation").GetProperty("calls")[0].GetProperty("assessment").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("&lang=en")]
    [InlineData("&lang=xx")]
    public async Task Ward_WithoutTurkish_StaysEnglish(string lang)
    {
        var bed = await Bed02(lang);

        Assert.Equal("Community-acquired pneumonia", bed.GetProperty("diagnosis").GetString());
        Assert.Equal("Restless, not eating, not herself", bed.GetProperty("escalation").GetProperty("concernNote").GetString());
    }

    [Fact]
    public async Task Patient_InTurkish_TranslatesEventsAndRecords()
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/patients/synthetic-02?at={At(17)}&lang=tr"));
        var root = doc.RootElement;

        Assert.Contains(root.GetProperty("events").EnumerateArray(), e => e.GetProperty("text").GetString() == "NEWS2 6: acil değerlendirme gerekli");
        Assert.Contains(root.GetProperty("escalationRecords").EnumerateArray(), r => r.GetProperty("text").GetString() == "Kan kültürleri, laktat, IV sıvı, oksijen 2 L/dk, antibiyotik");
        Assert.Equal("Toplum kökenli pnömoni", root.GetProperty("diagnosis").GetString());
    }
}
