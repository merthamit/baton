using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Baton.Api.Shift;
using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// Whole-branch review findings for the live shift (week 10).
public class ShiftReviewFixTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private HttpClient Client() => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
    }).CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> StartShift(HttpClient client, double hour)
    {
        var response = await client.PostAsJsonAsync("/api/shift", new { at = Start.AddHours(hour), speed = 1 });
        return (await Json(response)).GetProperty("shiftId").GetString()!;
    }

    private static async Task<string> Scale2Patient(HttpClient client)
    {
        var ward = await Json(await client.GetAsync("/api/ward"));
        return ward.GetProperty("beds").EnumerateArray()
            .First(b => b.GetProperty("latest").ValueKind == JsonValueKind.Object
                && b.GetProperty("latest").GetProperty("input").GetProperty("spO2Scale").GetString() == "Scale2")
            .GetProperty("patientId").GetString()!;
    }

    // I1: a Scale 2 patient's preview is scored with the patient's recorded decision.
    [Fact]
    public async Task Preview_ForAScale2Patient_HasATotal()
    {
        var client = Client();
        var patientId = await Scale2Patient(client);
        var shift = await StartShift(client, 14);

        var response = await client.PostAsJsonAsync("/api/news2", new
        {
            vitals = new { respirationRate = 20, spO2 = 89, oxygenLitresPerMinute = 2, systolicBp = 120, pulseRate = 90, temperatureCelsius = 37.0, consciousness = "Alert" },
            patientId,
            shift,
        });

        var score = await Json(response);
        Assert.Equal(JsonValueKind.Number, score.GetProperty("total").ValueKind);
        Assert.Equal(0, score.GetProperty("subScores").GetProperty("spO2").GetInt32()); // 88–92 % scores 0 on Scale 2
    }

    // I1 (Measure): noise on a Scale 2 bed never moves the score, judged with the decision.
    [Fact]
    public void TrueVitals_OnAScale2Bed_NoiseKeepsTheScore()
    {
        var bedIndex = Enumerable.Range(0, Ward.DefaultBeds)
            .First(i => CardShift.Shift(Ward.Plan(Ward.MaxBeds)[i].Card, Ward.Plan(Ward.MaxBeds)[i].ShiftHours).Scale == SpO2Scale.Scale2);
        var authorisation = new Scale2Authorisation("test", Start.AddHours(-1));
        for (var minute = 0; minute < 240; minute += 11)
        {
            var at = Start.AddHours(10).AddMinutes(minute);
            var noisy = TryCourse.TrueVitalsAt(Start, 42, bedIndex, Start.AddHours(10), [], at);
            var quiet = TryCourse.TrueVitalsAt(Start, 42, bedIndex, Start.AddHours(10), [], at, noiseLevel: 0);

            Assert.Equal(
                News2Calculator.Calculate(quiet.Vitals.ToNews2Input(SpO2Scale.Scale2, authorisation)).Total,
                News2Calculator.Calculate(noisy.Vitals.ToNews2Input(SpO2Scale.Scale2, authorisation)).Total);
        }
    }

    // I3: later in the night, scenario calls answered before the start (critical care has no plan record) stay off.
    [Theory]
    [InlineData(14)]
    [InlineData(18)]
    [InlineData(20)]
    public async Task DoctorsList_HasNoAnsweredScenarioCall(double hour)
    {
        var client = Client();
        var shift = await StartShift(client, hour);

        var calls = (await Json(await client.GetAsync($"/api/shift/{shift}/calls"))).GetProperty("calls").EnumerateArray().ToList();

        Assert.DoesNotContain(calls, c => c.GetProperty("call").GetProperty("arrivedAt").ValueKind != JsonValueKind.Null
            && c.GetProperty("call").GetProperty("calledAt").GetDateTimeOffset() < Start.AddHours(hour));
    }

    // Minor 1: a Try session and a shift together are refused.
    [Fact]
    public async Task TryAndShiftTogether_Is400()
    {
        var response = await Client().GetAsync($"/api/ward?try={new string('a', 32)}&shift={new string('b', 32)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Minor 2: a shift is a rehearsal too: its handover is not stored.
    [Fact]
    public async Task HandoverPost_WithShift_Is409()
    {
        var client = Client();
        var shift = await StartShift(client, 14);

        var response = await client.PostAsJsonAsync($"/api/handover/synthetic-02?shift={shift}", new { at = Start.AddHours(14) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

public class ShiftStoreReviewFixTests
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private static (WardShifts, WardShift) Make()
    {
        var shifts = new WardShifts(TimeProvider.System, new BatonOptionsSnapshot(ShiftStart, 42));
        return (shifts, shifts.Start(ShiftStart.AddHours(14), 1)!);
    }

    // Minor 5: one assessment and one plan per call.
    [Fact]
    public void SecondAssessmentOrPlan_IsRefused()
    {
        var (shifts, shift) = Make();
        shifts.Act(shift, "synthetic-02", ShiftActionKind.Call, null, EscalationTier.OnCallDoctor);
        shifts.Act(shift, "synthetic-02", ShiftActionKind.Arrive, null, null);
        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Assess, "a", null));

        Assert.Equal("already-assessed", shifts.Act(shift, "synthetic-02", ShiftActionKind.Assess, "b", null)?.Code);
        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Plan, "p", null));
        Assert.Equal("already-planned", shifts.Act(shift, "synthetic-02", ShiftActionKind.Plan, "q", null)?.Code);
    }

    // Minor 4 + the past path: a call left open before the start can be acknowledged, whatever order the past arrives in.
    [Fact]
    public void OpenCallFromBeforeTheStart_CanBeAcknowledged_WhateverTheOrder()
    {
        var (shifts, shift) = Make();
        EscalationRecord[] past =
        [
            new Called(ShiftStart.AddHours(13).AddMinutes(50), ScenarioEscalation.ScenarioNurse, "late", EscalationTier.SeniorDoctor),
            new Called(ShiftStart.AddHours(12), ScenarioEscalation.ScenarioNurse, "early", EscalationTier.OnCallDoctor),
            new Arrived(ShiftStart.AddHours(12).AddMinutes(20), ScenarioEscalation.OnCallDoctor, "early", false),
        ];

        Assert.Null(shifts.Act(shift, "synthetic-02", ShiftActionKind.Acknowledge, null, null, past));

        var ack = Assert.IsType<Acknowledged>(Assert.Single(shift.RecordsOf("synthetic-02")));
        Assert.Equal("late", ack.CallId);
        Assert.Equal(ScenarioEscalation.SeniorDoctor, ack.By);
    }
}
