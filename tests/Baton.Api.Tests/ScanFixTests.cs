#nullable disable warnings
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// Bugs found by the whole-project scan: Scale 2 before its decision, a space in `at`, and Try refusals that the UI
// can translate.
public class ScanFixTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private HttpClient Client(params (string Key, string Value)[] settings) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
        foreach (var (key, value) in settings)
        {
            b.UseSetting(key, value);
        }
    }).CreateClient();

    private static string Query(double hour) => Uri.EscapeDataString(ShiftSimulator.AtMinute(ShiftStart, hour).ToString("O"));

    [Fact]
    public async Task Scale2IsNotUsedBeforeItsDecision()
    {
        // 30-bed ward: some beds move to Scale 2 mid-shift. Every set before the decision has no authorised Scale 2,
        // exactly as the generator scored it.
        var ward = Ward.Generate(ShiftStart, 42, beds: 30);
        var client = Client(("Baton:Beds", "30"));
        foreach (var bed in ward.Where(b => b.Patient.Events.Any(e => e.Kind == ScenarioEventKind.Scale2Decision && e.Hour > 0)))
        {
            var decision = bed.Patient.Events.First(e => e.Kind == ScenarioEventKind.Scale2Decision).Hour;
            var before = bed.Patient.Measurements.Last(m => m.Hour < decision);
            using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/patients/{bed.PatientId}?at={Query(before.Hour)}"));
            var latest = doc.RootElement.GetProperty("points").EnumerateArray().Last();

            Assert.Equal(before.Score.Total, latest.GetProperty("total").ValueKind == JsonValueKind.Null ? null : latest.GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public async Task AtWithASpaceInsteadOfAnOffsetIsRefused()
    {
        var response = await Client().GetAsync("/api/ward?at=2026-11-23%2008:00");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnencodedPlusInTheOffsetStillWorks()
    {
        using var doc = JsonDocument.Parse(await Client().GetStringAsync("/api/ward?at=2026-11-23T23:30:00 03:00"));

        Assert.Equal(new DateTimeOffset(2026, 11, 23, 23, 30, 0, TimeSpan.FromHours(3)), doc.RootElement.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task TryRefusalsCarryACodeTheUiCanTranslate()
    {
        var client = Client();
        var started = await client.PostAsJsonAsync("/api/try", new { patientId = "synthetic-02", at = ShiftSimulator.AtMinute(ShiftStart, 12) });
        using var doc = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var session = doc.RootElement.GetProperty("sessionId").GetString();

        var refused = await client.PostAsJsonAsync($"/api/try/{session}/actions", new { kind = "Planned", at = ShiftSimulator.AtMinute(ShiftStart, 12), text = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("urn:baton:try:no-arrival", problem.RootElement.GetProperty("type").GetString());
    }
}
