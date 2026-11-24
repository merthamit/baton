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
        var (reader, release) = Register();
        try
        {
            while (true)
            {
                yield return await reader.ReadAsync(cancellationToken);
            }
        }
        finally
        {
            release();
        }
    }

    /// <summary>
    /// The SSE stream for one browser: "ready" once subscribed, then each change as "change", and a "ping" every
    /// <paramref name="keepAlive"/> so proxies keep the connection open. A Try session's changes go only to the stream
    /// opened for that session, and without the session id (it works like a key; the public stream never carries it).
    /// Ending the connection ends the stream quietly and removes the subscriber.
    /// </summary>
    public async IAsyncEnumerable<SseItem<object>> Stream(TimeSpan keepAlive, string? trySession, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (reader, release) = Register();
        try
        {
            yield return new SseItem<object>(new { at = DateTimeOffset.UtcNow }, "ready");
            while (true)
            {
                var wait = await Wait(reader, keepAlive, cancellationToken);
                if (wait == Waited.Ended)
                {
                    yield break;
                }

                if (wait == Waited.Quiet)
                {
                    yield return new SseItem<object>(new { at = DateTimeOffset.UtcNow }, "ping");
                    continue;
                }

                while (reader.TryRead(out var change))
                {
                    if (change.TrySession is null)
                    {
                        yield return new SseItem<object>(change, "change");
                    }
                    else if (change.TrySession == trySession)
                    {
                        yield return new SseItem<object>(change with { TrySession = null }, "change");
                    }
                }
            }
        }
        finally
        {
            release();
        }
    }

    private enum Waited
    {
        Ready,
        Quiet,
        Ended,
    }

    private static async Task<Waited> Wait(ChannelReader<WardChange> reader, TimeSpan keepAlive, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken).AsTask().WaitAsync(keepAlive, cancellationToken) ? Waited.Ready : Waited.Ended;
        }
        catch (TimeoutException)
        {
            return Waited.Quiet;
        }
        catch (OperationCanceledException)
        {
            return Waited.Ended;
        }
    }

    private (ChannelReader<WardChange> Reader, Action Release) Register()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<WardChange>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        subscribers[id] = channel;
        return (channel.Reader, () => subscribers.TryRemove(id, out _));
    }
}
