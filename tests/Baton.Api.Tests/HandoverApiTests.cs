#nullable disable warnings
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Baton.Api.Handover;
using Baton.Core.News2;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// The handover list (sickest first) and "Taken over" on the in-memory ward (seed 42). Bed 02 (sepsis): measured 23:15
// (NEWS2 6) and 00:15 (10). Every test gets a fresh host, so stored handovers never leak between tests.
// Shares the Firely validator, whose first schema load is not safe to run in parallel.
[Collection("Firely validator")]
public class HandoverApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private HttpClient NewClient() => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:Seed", "42");
    }).CreateClient();

    private static DateTimeOffset At(double hour) => ShiftSimulator.AtMinute(ShiftStart, hour);

    private static string Query(double hour) => Uri.EscapeDataString(At(hour).ToString("O"));

    private static async Task<JsonElement> List(HttpClient client, double hour, string extra = "")
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/handover?at={Query(hour)}{extra}"));
        return doc.RootElement.Clone();
    }

    private static JsonElement Row(JsonElement list, string bed) =>
        list.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("bed").GetString() == bed);

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string patientId, double hour, string query = "") =>
        client.PostAsJsonAsync($"/api/handover/{patientId}{query}", new { at = At(hour) });

    private static int Rank(JsonElement risk) => risk.ValueKind == JsonValueKind.Null ? -1 : risk.GetString() switch
    {
        "High" => 3,
        "Medium" => 2,
        "LowMedium" => 1,
        _ => 0,
    };

    [Fact]
    public async Task ListIsSortedSickestFirst()
    {
        var ranks = (await List(NewClient(), 17)).GetProperty("rows").EnumerateArray()
            .Select(r => Rank(r.GetProperty("sbar").GetProperty("currentRisk"))).ToList();

        Assert.Equal(8, ranks.Count);
        Assert.Equal(ranks.OrderByDescending(r => r), ranks);
        Assert.Equal(3, ranks[0]);
    }

    [Fact]
    public void WithinARiskMissingItemsComeFirst()
    {
        static HandoverRow RowOf(string bed, RiskLevel? risk, int? total, bool missing, int? peak = null) => new(
            $"synthetic-{bed}", bed, bed,
            new Sbar(At(24), At(12), "en", [], missing ? [new MissingItem(MissingKind.NoPlan, "No plan", [])] : [], "", total, risk, peak ?? total, risk),
            null, false);

        var ordered = HandoverService.Order(
        [
            RowOf("01", RiskLevel.Low, 0, false),
            RowOf("02", RiskLevel.Medium, 6, false),
            RowOf("03", RiskLevel.Medium, 5, true),
            RowOf("04", RiskLevel.High, 7, false),
            RowOf("05", null, null, true),
            RowOf("06", RiskLevel.Low, 2, false, peak: 6),
            RowOf("07", RiskLevel.Low, 2, false),
        ]);

        Assert.Equal(["04", "03", "02", "06", "07", "01", "05"], ordered.Select(r => r.Bed));
    }

    [Fact]
    public void AnIncompleteSetRanksByWhatItAlreadyShows()
    {
        HandoverRow RowOf(string bed, RiskLevel? risk, RiskLevel? floor) => new(
            $"synthetic-{bed}", bed, bed, new Sbar(At(24), At(12), "en", [], [], "", null, risk, null, floor), null, false);

        var ordered = HandoverService.Order([RowOf("01", RiskLevel.Low, RiskLevel.Low), RowOf("02", null, RiskLevel.Medium), RowOf("03", RiskLevel.LowMedium, RiskLevel.LowMedium)]);

        Assert.Equal(["02", "03", "01"], ordered.Select(r => r.Bed));
    }

    [Fact]
    public async Task RealDocumentValidatesAndResolvesEveryReference()
    {
        using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baton:Source", "InMemory");
            b.UseSetting("Baton:Seed", "42");
        });
        var ward = (WardService)app.Services.GetService(typeof(WardService))!;
        var built = (await ward.GetSbarInputAsync("synthetic-02", At(24), ShiftStart, null, null, CancellationToken.None))!.Value;
        var document = HandoverDocument.Build(SbarBuilder.Build(built.Input, null), built.Snapshot);

        var inside = document.Entry.Select(e => $"{e.Resource.TypeName}/{e.Resource.Id}").ToHashSet(StringComparer.Ordinal);
        var references = document.Entry.SelectMany(e => ReferencesIn(e.Resource)).Distinct().ToList();
        Assert.Contains(document.Entry, e => e.Resource is Hl7.Fhir.Model.Task);
        Assert.Contains(document.Entry, e => e.Resource is Hl7.Fhir.Model.Flag);
        Assert.All(references, r => Assert.Contains(r, inside));

        var errors = Baton.Tests.Shared.FhirValidation.Validator.Validate(document).Issue
            .Where(i => i.Severity is Hl7.Fhir.Model.OperationOutcome.IssueSeverity.Error or Hl7.Fhir.Model.OperationOutcome.IssueSeverity.Fatal)
            .Select(i => $"{i.Expression.FirstOrDefault()}: {i.Details?.Text ?? i.Diagnostics}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        HandoverDocumentTests.WriteExample(document);
    }

    private static IEnumerable<string> ReferencesIn(Hl7.Fhir.Model.Base element)
    {
        foreach (var (_, value) in element.EnumerateElements())
        {
            IEnumerable<Hl7.Fhir.Model.Base> children = value switch
            {
                Hl7.Fhir.Model.Base single => [single],
                IEnumerable<Hl7.Fhir.Model.Base> many => many,
                _ => [],
            };
            foreach (var child in children)
            {
                if (child is Hl7.Fhir.Model.ResourceReference { Reference: { } reference } && reference.Contains('/', StringComparison.Ordinal))
                {
                    yield return reference;
                }

                foreach (var nested in ReferencesIn(child))
                {
                    yield return nested;
                }
            }
        }
    }

    [Fact]
    public async Task ListWorksBeforeAnyObservation()
    {
        var list = await List(NewClient(), -1);

        Assert.All(list.GetProperty("rows").EnumerateArray(), row =>
            Assert.Contains(row.GetProperty("sbar").GetProperty("missing").EnumerateArray(), m => m.GetProperty("kind").GetString() == "NoObservations"));
    }

    [Fact]
    public async Task ConfirmStoresAndListShowsIt()
    {
        var client = NewClient();

        var response = await Confirm(client, "synthetic-02", 24);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("handover-synthetic-02-202611240800", created.RootElement.GetProperty("documentId").GetString());

        var list = await List(client, 24);
        Assert.Equal(At(24), Row(list, "02").GetProperty("confirmed").GetProperty("at").GetDateTimeOffset());
        Assert.Equal(1, list.GetProperty("confirmedCount").GetInt32());
        Assert.True(list.GetProperty("canConfirm").GetBoolean());
    }

    [Fact]
    public async Task SecondConfirmAtSameMinuteIs409()
    {
        var client = NewClient();
        await Confirm(client, "synthetic-02", 24);

        var again = await Confirm(client, "synthetic-02", 24);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Already taken over", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmationNotShownBeforeItsMoment()
    {
        var client = NewClient();
        await Confirm(client, "synthetic-02", 24);

        var earlier = await List(client, 23);

        Assert.Equal(JsonValueKind.Null, Row(earlier, "02").GetProperty("confirmed").ValueKind);
        Assert.Equal(0, earlier.GetProperty("confirmedCount").GetInt32());
    }

    [Fact]
    public async Task NewDataSinceHandoverAfterALaterMeasurement()
    {
        var client = NewClient();
        await Confirm(client, "synthetic-02", 15);   // 23:00, before the 23:15 measurement

        var later = Row(await List(client, 17), "02");

        Assert.True(later.GetProperty("newDataSinceHandover").GetBoolean());
    }

    [Fact]
    public async Task TryCannotConfirm()
    {
        var client = NewClient();
        var started = await client.PostAsJsonAsync("/api/try", new { patientId = "synthetic-02", at = At(12) });
        using var doc = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var session = doc.RootElement.GetProperty("sessionId").GetString();

        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(client, "synthetic-02", 13, $"?try={session}")).StatusCode);
        Assert.False((await List(client, 13, $"&try={session}")).GetProperty("canConfirm").GetBoolean());
    }

    [Fact]
    public async Task UnknownPatientIs404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Confirm(NewClient(), "synthetic-99", 24)).StatusCode);
    }

    [Fact]
    public async Task TurkishListHasTurkishSentences()
    {
        var row = Row(await List(NewClient(), 24, "&lang=tr"), "02");

        Assert.Equal("tr", row.GetProperty("sbar").GetProperty("language").GetString());
        var situation = row.GetProperty("sbar").GetProperty("sections")[0].GetProperty("sentences")[0].GetProperty("text").GetString();
        Assert.Matches(@"^NEWS2 \d+ (Düşük|Düşük–orta|Orta|Yüksek), \d{2}:\d{2}\.$", situation);
    }
}
