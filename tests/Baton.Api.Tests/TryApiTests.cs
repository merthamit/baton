using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Baton.Api.Tests;

// Try mode over HTTP: start a session for one bed, record actions, read the ward with ?try=, reset.
public class TryApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private HttpClient ClientWith(FakeTime? time = null, string source = "InMemory") => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", source);
        b.UseSetting("Baton:Seed", "42");
        b.UseSetting("Baton:FhirBaseUrl", "http://127.0.0.1:9/fhir");
        if (time is not null)
        {
            b.ConfigureServices(s => s.AddSingleton<TimeProvider>(time));
        }
    }).CreateClient();

    private static DateTimeOffset At(double hour) => ShiftSimulator.AtMinute(Start, hour);

    private static string Q(double hour) => Uri.EscapeDataString(At(hour).ToString("O"));

    private static async Task<string> StartTry(HttpClient client, string patientId = "synthetic-02", double hour = 14)
    {
        var response = await client.PostAsJsonAsync("/api/try", new { patientId, at = At(hour) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("sessionId").GetString()!;
    }

    private static Task<HttpResponseMessage> Act(HttpClient client, string session, string kind, double hour, string? text = null, string? tier = null) =>
        client.PostAsJsonAsync($"/api/try/{session}/actions", new { kind, at = At(hour), text, tier });

    private static async Task<JsonElement> Bed(HttpClient client, double hour, string bed, string? session = null)
    {
        var tryQuery = session is null ? "" : $"&try={session}";
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/ward?at={Q(hour)}{tryQuery}"));
        return doc.RootElement.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("bed").GetString() == bed).Clone();
    }

    [Fact]
    public async Task StartAddReset_Lifecycle()
    {
        var client = ClientWith();
        var session = await StartTry(client);

        Assert.Equal(HttpStatusCode.OK, (await Act(client, session, "Concern", 14.5, "Restless")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/try/{session}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ward?at={Q(15)}&try={session}")).StatusCode);
    }

    [Fact]
    public async Task Ward_WithTry_ShowsTheUsersCall()
    {
        var client = ClientWith();
        var session = await StartTry(client);
        await Act(client, session, "Call", 17, tier: "OnCallDoctor");

        var chain = (await Bed(client, 17.1667, "02", session)).GetProperty("escalation"); // 01:10

        Assert.Equal(At(17), chain.GetProperty("openCall").GetProperty("calledAt").GetDateTimeOffset());
        Assert.Equal(10, chain.GetProperty("minutesWaited").GetInt32());
    }

    [Fact]
    public async Task OtherBeds_Unchanged()
    {
        var client = ClientWith();
        var session = await StartTry(client);
        await Act(client, session, "Call", 17, tier: "OnCallDoctor");

        Assert.Equal((await Bed(client, 17.5, "04")).ToString(), (await Bed(client, 17.5, "04", session)).ToString());
    }

    [Fact]
    public async Task LateCall_RaisesLaterScores()
    {
        var client = ClientWith();
        var session = await StartTry(client);
        await Act(client, session, "Call", 17, tier: "OnCallDoctor");

        var watch = (await Bed(client, 18.5, "02")).GetProperty("latest").GetProperty("total").GetInt32();
        var tried = (await Bed(client, 18.5, "02", session)).GetProperty("latest").GetProperty("total").GetInt32();

        Assert.True(tried > watch, $"{tried} vs {watch}");
    }

    [Fact]
    public async Task Hapi_Returns409()
    {
        var response = await ClientWith(source: "Hapi").PostAsJsonAsync("/api/try", new { patientId = "synthetic-02", at = At(14) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOrExpired_Returns404()
    {
        var time = new FakeTime();
        var client = ClientWith(time);
        var session = await StartTry(client);
        time.Now = time.Now.AddHours(2).AddMinutes(1);

        var expired = await client.GetAsync($"/api/ward?at={Q(15)}&try={session}");
        var unknown = await Act(client, "nope", "Concern", 15);

        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Contains("expired", await expired.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task ActionBeforeLast_Returns400()
    {
        var client = ClientWith();
        var session = await StartTry(client);
        await Act(client, session, "Concern", 15);

        var response = await Act(client, session, "Concern", 14.5);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssessedBeforeArrival_Returns400()
    {
        var client = ClientWith();
        var session = await StartTry(client);
        await Act(client, session, "Call", 17, tier: "OnCallDoctor");

        var early = await Act(client, session, "Assessed", 17.1, "Septic"); // arrival is 01:21
        var onTime = await Act(client, session, "Assessed", 17.5, "Septic");

        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.Equal(HttpStatusCode.OK, onTime.StatusCode);
    }

    [Fact]
    public async Task ActionOutsideShift_Returns400()
    {
        var client = ClientWith();
        var session = await StartTry(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await Act(client, session, "Concern", 24.5)).StatusCode);
    }

    [Fact]
    public async Task MaxSessions_DropsOldest()
    {
        var client = ClientWith();
        var first = await StartTry(client);
        for (var i = 0; i < 50; i++)
        {
            await StartTry(client);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ward?at={Q(15)}&try={first}")).StatusCode);
    }

    [Fact]
    public async Task Health_ReportsTryAvailable()
    {
        using var memory = JsonDocument.Parse(await ClientWith().GetStringAsync("/health"));

        Assert.True(memory.RootElement.GetProperty("tryAvailable").GetBoolean());
    }
}
