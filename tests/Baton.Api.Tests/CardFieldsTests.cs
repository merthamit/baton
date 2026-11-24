using System.Net;
using System.Text.Json;
using Baton.Core.News2;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// What the 4B nurse card needs from the API: escalation state, drivers, next observation due and overdue time,
// the shift window, age and diagnosis. Bed 02 (sepsis, seed 42): concern 20:15, alert 23:15, doctor called 23:19,
// arrived 23:40, critical care called 00:20; measured 23:15 (NEWS2 6) and 00:15 (10).
public class CardFieldsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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

    private static string Query(double hour) => Uri.EscapeDataString(At(hour).ToString("O"));

    private async Task<JsonElement> Ward(double hour, HttpClient? client = null)
    {
        using var doc = JsonDocument.Parse(await (client ?? ClientWith()).GetStringAsync($"/api/ward?at={Query(hour)}"));
        return doc.RootElement.Clone();
    }

    private static JsonElement Bed(JsonElement ward, string bed) =>
        ward.GetProperty("beds").EnumerateArray().Single(b => b.GetProperty("bed").GetString() == bed);

    [Fact]
    public void Escalation_CalledNotArrived_CountsWaitingMinutes()
    {
        var events = new[] { new WardEvent(At(16.0833), ScenarioEventKind.DoctorCalled, "called") };

        var escalation = Escalation.From(events, At(16.0833).AddMinutes(20));

        Assert.Equal(events[0].At, escalation.DoctorCalledAt);
        Assert.Equal(20, escalation.WaitingMinutes);
        Assert.Null(escalation.DoctorArrivedAt);
    }

    [Fact]
    public void Escalation_Arrived_NoWaiting()
    {
        var events = new[]
        {
            new WardEvent(At(15), ScenarioEventKind.DoctorCalled, "called"),
            new WardEvent(At(15.5), ScenarioEventKind.DoctorArrived, "arrived"),
        };

        var escalation = Escalation.From(events, At(16));

        Assert.Equal(At(15.5), escalation.DoctorArrivedAt);
        Assert.Null(escalation.WaitingMinutes);
    }

    [Fact]
    public void Escalation_IgnoresEventsAfterAt()
    {
        var events = new[] { new WardEvent(At(15), ScenarioEventKind.Concern, "worried") };

        Assert.Null(Escalation.From(events, At(14)).ConcernRaisedAt);
    }

    [Fact]
    public void Drivers_OrderedByScoreThenChart()
    {
        var subScores = new Dictionary<News2Parameter, int?>
        {
            [News2Parameter.RespirationRate] = 2,
            [News2Parameter.SpO2] = 0,
            [News2Parameter.SupplementalOxygen] = 2,
            [News2Parameter.SystolicBp] = 1,
            [News2Parameter.PulseRate] = 3,
            [News2Parameter.Consciousness] = 0,
            [News2Parameter.Temperature] = null,
        };

        Assert.Equal(
            [News2Parameter.PulseRate, News2Parameter.RespirationRate, News2Parameter.SupplementalOxygen, News2Parameter.SystolicBp],
            WardService.DriversOf(subScores));
    }

    [Fact]
    public async Task Ward_Bed02_HasConcernAndDoctorCalled()
    {
        var bed = Bed(await Ward(15.5), "02"); // 23:30
        var escalation = bed.GetProperty("escalation");

        Assert.Equal(At(12.25), escalation.GetProperty("concernRaisedAt").GetDateTimeOffset());
        Assert.Equal(At(15.25).AddMinutes(4), escalation.GetProperty("doctorCalledAt").GetDateTimeOffset());
        Assert.Equal(11, escalation.GetProperty("waitingMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, escalation.GetProperty("doctorArrivedAt").ValueKind);
        Assert.True(bed.GetProperty("eventsAvailable").GetBoolean());
        Assert.True(bed.GetProperty("concernsKnown").GetBoolean());
        Assert.All(bed.GetProperty("events").EnumerateArray(), e => Assert.True(e.GetProperty("at").GetDateTimeOffset() <= At(15.5)));
    }

    [Fact]
    public async Task Ward_Drivers_MatchTheLatestSubScores()
    {
        var bed = Bed(await Ward(15.5), "02");
        var subScores = bed.GetProperty("latest").GetProperty("subScores").EnumerateObject()
            .ToDictionary(p => Enum.Parse<News2Parameter>(p.Name, ignoreCase: true), p => p.Value.ValueKind == JsonValueKind.Null ? (int?)null : p.Value.GetInt32());

        Assert.Equal(
            WardService.DriversOf(subScores).Select(p => p.ToString()),
            bed.GetProperty("drivers").EnumerateArray().Select(d => d.GetString()));
        Assert.NotEmpty(bed.GetProperty("drivers").EnumerateArray());
    }

    [Fact]
    public async Task Ward_NextDue_FollowsTheSchedule()
    {
        var bed = Bed(await Ward(15.5), "02"); // latest 23:15, urgent response → hourly

        Assert.Equal(At(16.25), bed.GetProperty("nextDue").GetDateTimeOffset());
        Assert.Equal(0, bed.GetProperty("overdueMinutes").GetInt32());
    }

    [Fact]
    public async Task Ward_LongAfterLastObservation_IsOverdue()
    {
        var bed = Bed(await Ward(30), "01"); // bed 01 scores 0, last measured at 20:00 → due 08:00, at 14:00

        Assert.Equal(At(24), bed.GetProperty("nextDue").GetDateTimeOffset());
        Assert.Equal(360, bed.GetProperty("overdueMinutes").GetInt32());
    }

    [Fact]
    public async Task Ward_BeforeFirstMeasurement_NoDue()
    {
        var bed = Bed(await Ward(-1), "02");

        Assert.Equal(JsonValueKind.Null, bed.GetProperty("nextDue").ValueKind);
        Assert.Equal(JsonValueKind.Null, bed.GetProperty("overdueMinutes").ValueKind);
        Assert.Empty(bed.GetProperty("drivers").EnumerateArray());
    }

    [Fact]
    public async Task Ward_HasShiftWindow()
    {
        var ward = await Ward(15.5);

        Assert.Equal(Start, ward.GetProperty("shiftStart").GetDateTimeOffset());
        Assert.Equal(Start.AddHours(24), ward.GetProperty("shiftEnd").GetDateTimeOffset());
    }

    [Fact]
    public async Task Ward_AgeAndDiagnosis()
    {
        var bed = Bed(await Ward(15.5), "02");

        Assert.Equal(71, bed.GetProperty("age").GetInt32());
        Assert.Equal(Baton.Scenarios.Ward.Generate(Start, 42)[1].Diagnosis, bed.GetProperty("diagnosis").GetString());
    }

    [Fact]
    public async Task Patient_HasAgeDiagnosisAndEvents()
    {
        using var doc = JsonDocument.Parse(await ClientWith().GetStringAsync($"/api/patients/synthetic-02?at={Query(23.9)}"));
        var root = doc.RootElement;

        Assert.Equal(71, root.GetProperty("age").GetInt32());
        Assert.True(root.GetProperty("eventsAvailable").GetBoolean());
        Assert.Contains(root.GetProperty("events").EnumerateArray(), e => e.GetProperty("kind").GetString() == "CriticalCareCalled");
    }

    [Fact]
    public async Task Ward_ThirtyBeds_WhenConfigured()
    {
        var ward = await Ward(15.5, ClientWith(("Baton:Beds", "30")));

        Assert.Equal(30, ward.GetProperty("beds").GetArrayLength());
    }

    [Fact]
    public async Task Ward_FhirDown_Returns503Problem()
    {
        var client = ClientWith(("Baton:Source", "Hapi"), ("Baton:FhirBaseUrl", "http://127.0.0.1:9/fhir"));

        var response = await client.GetAsync($"/api/ward?at={Query(15.5)}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FHIR server unreachable", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Ward_UtcAt_IsAnsweredInWardTime()
    {
        var utc = Uri.EscapeDataString(At(15.5).ToUniversalTime().ToString("O"));
        using var doc = JsonDocument.Parse(await ClientWith().GetStringAsync($"/api/ward?at={utc}"));
        var bed = Bed(doc.RootElement, "02");

        Assert.Equal(TimeSpan.FromHours(3), doc.RootElement.GetProperty("at").GetDateTimeOffset().Offset);
        Assert.EndsWith("+03:00", bed.GetProperty("latest").GetProperty("at").GetString());
        Assert.EndsWith("+03:00", bed.GetProperty("nextDue").GetString());
        Assert.EndsWith("+03:00", bed.GetProperty("escalation").GetProperty("concernRaisedAt").GetString());
    }
}
