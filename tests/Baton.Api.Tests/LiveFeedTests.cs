using System.Net.Http.Json;
using Baton.Api.Live;
using Baton.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// The live board's change feed: every subscriber gets each change, a slow one never blocks the publisher, and a
// closed one is removed. GET /api/live streams the changes as Server-Sent Events.
public class LiveFeedTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 8, 0, 0, TimeSpan.FromHours(3));

    private static WardChange Change(int n) => new("Fhir", $"synthetic-{n:00}", null, At);

    private static async Task<List<WardChange>> Take(IAsyncEnumerable<WardChange> stream, int count)
    {
        var items = new List<WardChange>();
        await foreach (var item in stream)
        {
            items.Add(item);
            if (items.Count == count)
            {
                break;
            }
        }

        return items;
    }

    [Fact]
    public async Task PublishedChangeReachesEverySubscriber()
    {
        var feed = new WardChangeFeed();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var a = feed.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var b = feed.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var readA = a.MoveNextAsync();
        var readB = b.MoveNextAsync();

        feed.Publish(Change(2));

        Assert.True(await readA);
        Assert.True(await readB);
        Assert.Equal("synthetic-02", a.Current.PatientId);
        Assert.Equal("synthetic-02", b.Current.PatientId);
        await a.DisposeAsync();
        await b.DisposeAsync();
    }

    [Fact]
    public async Task SlowSubscriberDoesNotBlockPublisher()
    {
        var feed = new WardChangeFeed();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = feed.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var first = reader.MoveNextAsync(); // registers the subscriber

        for (var i = 0; i < 200; i++)
        {
            feed.Publish(Change(i));
        }

        // The pending read took the first change; the buffer kept the newest Capacity of the rest.
        Assert.True(await first);
        Assert.Equal("synthetic-00", reader.Current.PatientId);
        var kept = new List<string?>();
        while (kept.Count < WardChangeFeed.Capacity && await reader.MoveNextAsync())
        {
            kept.Add(reader.Current.PatientId);
        }

        Assert.Equal($"synthetic-{200 - WardChangeFeed.Capacity}", kept[0]);
        Assert.Equal("synthetic-199", kept[^1]);
        await reader.DisposeAsync();
    }

    [Fact]
    public async Task CancelledSubscriberIsRemoved()
    {
        var feed = new WardChangeFeed();
        using var cts = new CancellationTokenSource();
        var reader = feed.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var pending = reader.MoveNextAsync();
        Assert.Equal(1, feed.Subscribers);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await reader.DisposeAsync();

        Assert.Equal(0, feed.Subscribers);
    }

    [Fact]
    public async Task LiveEndpointStreamsAHandoverChange()
    {
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baton:Source", "InMemory");
            b.UseSetting("Baton:Seed", "42");
        }).CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        using var response = await client.GetAsync("/api/live", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        while (await reader.ReadLineAsync(cts.Token) is { } first && first != "event: ready")
        {
        }

        var confirmed = await client.PostAsJsonAsync("/api/handover/synthetic-02", new { at = ShiftSimulator.AtMinute(new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3)), 24) }, cts.Token);
        Assert.True(confirmed.IsSuccessStatusCode);

        string? line;
        var sawEvent = false;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            sawEvent |= line == "event: change";
            if (sawEvent && line.StartsWith("data:", StringComparison.Ordinal))
            {
                Assert.Contains("Handover", line, StringComparison.Ordinal);
                Assert.Contains("synthetic-02", line, StringComparison.Ordinal);
                return;
            }
        }

        Assert.Fail("No change event arrived.");
    }
}
