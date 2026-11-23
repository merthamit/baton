using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// Regression tests for the week 4 whole-branch review.
public class ReviewFindingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient Client => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
    }).CreateClient();

    [Fact]
    public async Task At_WithAnUnencodedPlus_IsReadAsTheOffset()
    {
        var raw = await Client.GetStringAsync("/api/ward?at=2026-11-23T23:30:00+03:00");
        var encoded = await Client.GetStringAsync("/api/ward?at=2026-11-23T23:30:00%2B03:00");

        Assert.Equal(encoded, raw);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 23, 30, 0, TimeSpan.FromHours(3)), doc.RootElement.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task At_WithoutAnOffset_IsRejected()
    {
        var response = await Client.GetAsync("/api/ward?at=2026-11-23T23:30");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("offset", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task At_Garbage_IsRejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/ward?at=yesterday")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/patients/synthetic-y-02?at=yesterday")).StatusCode);
    }

    [Fact]
    public async Task Ward_SaysWhichSourceAndMomentItShows()
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync("/api/ward"));

        Assert.Equal("InMemory", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 23, 30, 0, TimeSpan.FromHours(3)), doc.RootElement.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task Health_ReportsTheSource()
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync("/health"));

        Assert.Equal("InMemory", doc.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task SubScoreKeys_AreCamelCaseLikeTheRestOfTheJson()
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync("/api/ward"));
        var subScores = doc.RootElement.GetProperty("beds")[0].GetProperty("latest").GetProperty("subScores");

        Assert.True(subScores.TryGetProperty("respirationRate", out _));
    }

    [Fact]
    public void UnknownSource_FailsAtStartupNotOnEveryRequest()
    {
        var bad = factory.WithWebHostBuilder(b => b.UseSetting("Baton:Source", "Nonsense"));

        Assert.ThrowsAny<Exception>(() => bad.CreateClient());
    }

    [Fact]
    public async Task Source_IsCaseInsensitive()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Baton:Source", "inmemory")).CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ward")).StatusCode);
    }
}
