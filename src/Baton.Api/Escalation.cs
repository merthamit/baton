using Baton.Scenarios;

namespace Baton.Api;

/// <summary>
/// Where the escalation stands at a moment, for the nurse card: the latest concern flag, doctor call, doctor arrival
/// and critical care call at or before that moment, and how long the doctor has been waited for.
/// </summary>
public sealed record Escalation(
    DateTimeOffset? ConcernRaisedAt,
    DateTimeOffset? DoctorCalledAt,
    DateTimeOffset? DoctorArrivedAt,
    DateTimeOffset? CriticalCareCalledAt,
    int? WaitingMinutes)
{
    public static Escalation None { get; } = new(null, null, null, null, null);

    /// <summary>
    /// Builds the state from events. <see cref="WaitingMinutes"/> is whole minutes since the latest doctor call, and
    /// only while no arrival at or after that call has been recorded.
    /// </summary>
    public static Escalation From(IReadOnlyList<WardEvent> events, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(events);

        DateTimeOffset? Latest(ScenarioEventKind kind) =>
            events.Where(e => e.Kind == kind && e.At <= at).Select(e => (DateTimeOffset?)e.At).Max();

        var called = Latest(ScenarioEventKind.DoctorCalled);
        var arrived = Latest(ScenarioEventKind.DoctorArrived);
        int? waiting = called is { } c && (arrived is null || arrived < c)
            ? (int)Math.Floor((at - c).TotalMinutes)
            : null;

        return new Escalation(Latest(ScenarioEventKind.Concern), called, arrived, Latest(ScenarioEventKind.CriticalCareCalled), waiting);
    }
}
