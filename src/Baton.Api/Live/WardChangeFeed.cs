using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Baton.Api.Live;

/// <summary>
/// "Something changed, refetch": the only thing a live event says. It never carries clinical content; the board asks
/// the API again and the API scores as always. <see cref="TrySession"/> marks a change inside one Try session.
/// </summary>
public sealed record WardChange(string Kind, string? PatientId, string? TrySession, DateTimeOffset At);

/// <summary>
/// An in-process broadcast of <see cref="WardChange"/>s. Every subscriber has its own bounded buffer that drops the
/// oldest change when full, so a slow browser never blocks a publisher; a cancelled subscriber is removed.
/// </summary>
public sealed class WardChangeFeed
{
    public const int Capacity = 64;

    private readonly ConcurrentDictionary<Guid, Channel<WardChange>> subscribers = new();

    public int Subscribers => subscribers.Count;

    public void Publish(WardChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(change);
        }
    }

    public async IAsyncEnumerable<WardChange> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<WardChange>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        subscribers[id] = channel;
        try
        {
            while (true)
            {
                yield return await channel.Reader.ReadAsync(cancellationToken);
            }
        }
        finally
        {
            subscribers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// The SSE stream for one browser: "ready" once subscribed, then each change as "change", and a "ping" every
    /// <paramref name="keepAlive"/> so proxies keep the connection open.
    /// </summary>
    public async IAsyncEnumerable<SseItem<object>> Stream(TimeSpan keepAlive, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var changes = Subscribe(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var next = changes.MoveNextAsync().AsTask();
        yield return new SseItem<object>(new { at = DateTimeOffset.UtcNow }, "ready");
        while (!cancellationToken.IsCancellationRequested)
        {
            var tick = Task.Delay(keepAlive, cancellationToken);
            var done = await Task.WhenAny(next, tick);
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (done == next)
            {
                if (!await next)
                {
                    yield break;
                }

                yield return new SseItem<object>(changes.Current, "change");
                next = changes.MoveNextAsync().AsTask();
            }
            else
            {
                yield return new SseItem<object>(new { at = DateTimeOffset.UtcNow }, "ping");
            }
        }
    }
}
