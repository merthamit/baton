using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// The API runs on the in-memory synthetic source (seed 42); its scores must equal the generator's.
public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));
    private static readonly IReadOnlyList<WardBed> Generated = Ward.Generate(ShiftStart, 42);

    private HttpClient Client => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
    }).CreateClient();

    private static string At(double hour) => Uri.EscapeDataString(ShiftSimulator.AtMinute(ShiftStart, hour).ToString("O"));

    private static Measurement? LastBefore(WardBed bed, double hour) =>
        bed.Patient.Measurements.LastOrDefault(m => m.Hour <= hour + 1e-9);

    [Theory]
    [InlineData(15.5)]  // 23:30
    [InlineData(15.25)] // 23:15, exactly bed 02's measurement time
    [InlineData(20)]    // 04:00
    public async Task Ward_ScoresMatchTheGenerator(double hour)
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/ward?at={At(hour)}"));
        var beds = doc.RootElement.GetProperty("beds").EnumerateArray().ToList();

        Assert.Equal(8, beds.Count);
        foreach (var generated in Generated)
        {
            var bed = beds.Single(b => b.GetProperty("bed").GetString() == generated.Bed);
            var expected = LastBefore(generated, hour)!;
            var latest = bed.GetProperty("latest");
            Assert.Equal(expected.Score.Total, latest.GetProperty("total").GetInt32());
            Assert.Equal(expected.Score.Risk.ToString(), latest.GetProperty("risk").GetString());
            Assert.Equal(expected.At, latest.GetProperty("at").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task Ward_BeforeTheFirstMeasurement_HasNoLatestScore()
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/ward?at={At(-1)}"));

        Assert.All(doc.RootElement.GetProperty("beds").EnumerateArray(), b => Assert.Equal(JsonValueKind.Null, b.GetProperty("latest").ValueKind));
    }

    [Fact]
    public async Task Ward_GivesThePreviousScoreForTheTrend()
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/ward?at={At(15.5)}"));
        var y02 = doc.RootElement.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("bed").GetString() == "02");

        Assert.Equal(Generated[1].Patient.Measurements.Single(m => m.Hour == 13.25).Score.Total, y02.GetProperty("previous").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Patient_SeriesMatchesTheGenerator()
    {
        var bed = Generated[4]; // bed 05, COPD on Scale 2
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/patients/{bed.PatientId}?at={At(23.9)}"));
        var points = doc.RootElement.GetProperty("points").EnumerateArray().ToList();

        Assert.Equal(bed.Patient.Measurements.Count, points.Count);
        Assert.Equal(bed.Patient.Measurements.Select(m => m.Score.Total), points.Select(p => (int?)p.GetProperty("total").GetInt32()));
        Assert.Equal("Scale2", doc.RootElement.GetProperty("patient").GetProperty("spO2Scale").GetString());
    }

    [Fact]
    public async Task Patient_Unknown_Is404()
    {
        var response = await Client.GetAsync($"/api/patients/nobody?at={At(10)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ward_WithoutAt_UsesTheConfiguredDefault()
    {
        var response = await Client.GetAsync("/api/ward");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(8, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("beds").GetArrayLength());
    }
}
