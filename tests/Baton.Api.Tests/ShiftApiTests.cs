using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Baton.Api.Tests;

// The real ward flow over HTTP: start a private shift, measure and enter a set, call, the doctor acknowledges and arrives.
public class ShiftApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset Real = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Real;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private HttpClient ClientWith(FakeTime? time = null, string source = "InMemory") => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", source);
        b.UseSetting("Baton:Seed", "42");
        b.UseSetting("Baton:FhirBaseUrl", "http://127.0.0.1:9/fhir");
        b.ConfigureServices(s => s.AddSingleton<TimeProvider>(time ?? new FakeTime()));
    }).CreateClient();

    private static object Vitals(int rr = 24, int spo2 = 93) => new
    {
        respirationRate = rr,
        spO2 = spo2,
        oxygenLitresPerMinute = 0,
        systolicBp = 100,
        pulseRate = 112,
        temperatureCelsius = 38.6,
        consciousness = "Alert",
    };

    private static async Task<string> StartShift(HttpClient client, double hour = 14, int speed = 10)
    {
        var response = await client.PostAsJsonAsync("/api/shift", new { at = Start.AddHours(hour), speed });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("shiftId").GetString()!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Bed(HttpClient client, string shift, string patientId)
    {
        var ward = await Json(await client.GetAsync($"/api/ward?shift={shift}"));
        return ward.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("patientId").GetString() == patientId);
    }

    [Fact]
    public async Task Start_ReturnsTheRunningClock()
    {
        var time = new FakeTime();
        var client = ClientWith(time);
        var shift = await StartShift(client);
        time.Now = Real.AddMinutes(3);

        var state = await Json(await client.GetAsync($"/api/shift/{shift}"));

        Assert.Equal(Start.AddHours(14).AddMinutes(30), state.GetProperty("now").GetDateTimeOffset());
        Assert.Equal(10, state.GetProperty("speed").GetInt32());
    }

    [Fact]
    public async Task Start_NeedsTheInMemorySource()
    {
        var response = await ClientWith(source: "Hapi").PostAsJsonAsync("/api/shift", new { at = Start.AddHours(14), speed = 10 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Start_WithAnUnknownSpeed_Is400()
    {
        var response = await ClientWith().PostAsJsonAsync("/api/shift", new { at = Start.AddHours(14), speed = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ward_WithShift_AnswersAtWardNow_AndShowsTheBedsideSet()
    {
        var time = new FakeTime();
        var client = ClientWith(time);
        var shift = await StartShift(client);
        time.Now = Real.AddMinutes(1); // 14:10

        var saved = await client.PostAsJsonAsync($"/api/shift/{shift}/observations", new { patientId = "synthetic-02", vitals = Vitals() });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = await Json(saved);
        Assert.Equal(Start.AddHours(14).AddMinutes(10), result.GetProperty("latest").GetProperty("at").GetDateTimeOffset());
        Assert.True(result.GetProperty("latest").GetProperty("total").GetInt32() >= 5);

        var ward = await Json(await client.GetAsync($"/api/ward?shift={shift}&at=2026-11-23T09:00:00%2B03:00"));
        Assert.Equal(Start.AddHours(14).AddMinutes(10), ward.GetProperty("at").GetDateTimeOffset()); // `at` is ignored
        var bed = ward.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("patientId").GetString() == "synthetic-02");
        Assert.Equal(Start.AddHours(14).AddMinutes(10), bed.GetProperty("latest").GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task OutOfRangeValue_Is400_WithTheField()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        var response = await client.PostAsJsonAsync($"/api/shift/{shift}/observations", new { patientId = "synthetic-02", vitals = Vitals(spo2: 130) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("urn:baton:shift:out-of-range", problem.GetProperty("type").GetString());
        Assert.Equal("spO2", problem.GetProperty("field").GetString());
    }

    [Fact]
    public async Task UnknownPatient_Is404()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        var response = await client.PostAsJsonAsync($"/api/shift/{shift}/observations", new { patientId = "synthetic-99", vitals = Vitals() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownShift_Is404()
    {
        var client = ClientWith();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/shift/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/ward?shift=nope")).StatusCode);
    }

    [Fact]
    public async Task Measure_GivesTheBedsCurrentVitals()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        var measured = await Json(await client.GetAsync($"/api/shift/{shift}/patients/synthetic-02/measure"));

        Assert.InRange(measured.GetProperty("vitals").GetProperty("respirationRate").GetInt32(), 3, 80);
        Assert.Equal(Start.AddHours(14), measured.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task CallAcknowledgeArrive_ShowsOnTheBoard_AndInTheDoctorsList()
    {
        var time = new FakeTime();
        var client = ClientWith(time);
        var shift = await StartShift(client);
        Task<HttpResponseMessage> Act(string kind, string? tier = null, string? text = null) =>
            client.PostAsJsonAsync($"/api/shift/{shift}/actions", new { patientId = "synthetic-03", kind, tier, text });

        Assert.Equal(HttpStatusCode.OK, (await Act("Call", "OnCallDoctor")).StatusCode);
        var calls = await Json(await client.GetAsync($"/api/shift/{shift}/calls"));
        var call = calls.GetProperty("calls").EnumerateArray().Single(c => c.GetProperty("patientId").GetString() == "synthetic-03");
        Assert.Equal("Called", call.GetProperty("step").GetString());

        time.Now = Real.AddMinutes(1);
        Assert.Equal(HttpStatusCode.OK, (await Act("Acknowledge")).StatusCode);
        var chain = (await Bed(client, shift, "synthetic-03")).GetProperty("escalation");
        Assert.Equal("Acknowledged", chain.GetProperty("step").GetString());
        Assert.Equal(Start.AddHours(14).AddMinutes(10), chain.GetProperty("openCall").GetProperty("acknowledgedAt").GetDateTimeOffset());

        Assert.Equal(HttpStatusCode.OK, (await Act("Arrive")).StatusCode);
        Assert.Equal("Arrived", (await Bed(client, shift, "synthetic-03")).GetProperty("escalation").GetProperty("step").GetString());
    }

    [Fact]
    public async Task RefusedAction_Is400_WithItsCode()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        var response = await client.PostAsJsonAsync($"/api/shift/{shift}/actions", new { patientId = "synthetic-03", kind = "Acknowledge" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("urn:baton:shift:no-open-call", (await Json(response)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task NotDone_ShowsOnTheBed()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        var response = await client.PostAsJsonAsync($"/api/shift/{shift}/not-done", new { patientId = "synthetic-04", reason = "CouldNotBeObtained" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var notDone = (await Bed(client, shift, "synthetic-04")).GetProperty("notDone");
        Assert.Equal("CouldNotBeObtained", notDone.GetProperty("reason").GetString());
        Assert.True(notDone.GetProperty("urgent").GetBoolean());
    }

    [Fact]
    public async Task Speed_CanChange()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/shift/{shift}/speed", new { speed = 60 })).StatusCode);
        Assert.Equal(60, (await Json(await client.GetAsync($"/api/shift/{shift}"))).GetProperty("speed").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/shift/{shift}/speed", new { speed = 2 })).StatusCode);
    }

    [Fact]
    public async Task ScorePreview_UsesTheEngine_WithoutStoring()
    {
        var client = ClientWith();

        var response = await client.PostAsJsonAsync("/api/news2", new { vitals = Vitals(rr: 26, spo2: 91), scale = "Scale1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var score = await Json(response);
        Assert.True(score.GetProperty("total").GetInt32() >= 7);
        Assert.Equal("High", score.GetProperty("risk").GetString());
        Assert.Equal(3, score.GetProperty("subScores").GetProperty("respirationRate").GetInt32());
    }

    [Fact]
    public async Task End_RemovesTheShift()
    {
        var client = ClientWith();
        var shift = await StartShift(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/shift/{shift}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/shift/{shift}")).StatusCode);
    }
}
