using System.Collections.Concurrent;
using System.Globalization;
using Baton.Core.Escalation;
using Baton.Scenarios;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Baton.Api;

/// <summary>One visitor playing the nurse for one bed.</summary>
public sealed class TrySession(string id, string patientId, int bedIndex, DateTimeOffset startedAt)
{
    private readonly List<TryAction> actions = [];

    public string Id { get; } = id;

    public string PatientId { get; } = patientId;

    public int BedIndex { get; } = bedIndex;

    public DateTimeOffset StartedAt { get; } = startedAt;

    private long lastUsedTicks;

    /// <summary>When the session was last used (UTC). Read and written atomically: requests arrive in parallel.</summary>
    public DateTimeOffset LastUsed
    {
        get => new(Interlocked.Read(ref lastUsedTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref lastUsedTicks, value.UtcTicks);
    }

    public object Gate { get; } = new();

    public IReadOnlyList<TryAction> Actions
    {
        get
        {
            lock (Gate)
            {
                return [.. actions];
            }
        }
    }

    public DateTimeOffset LastActionAt => Actions.Count == 0 ? StartedAt : Actions.Max(a => a.At);

    internal void Add(TryAction action)
    {
        lock (Gate)
        {
            actions.Add(action);
        }
    }
}

/// <summary>
/// Try sessions, kept in API memory: they expire two hours after their last use and at most 50 are kept (the least
/// recently used is dropped). Try needs the in-memory source, because it re-runs the simulation from the user's timing.
/// </summary>
public sealed class TrySessions(TimeProvider time, BatonOptionsSnapshot settings)
{
    public const int MaxSessions = 50;

    /// <summary>Per-session limits: every request re-runs the simulation from the actions, so keep them bounded.</summary>
    public const int MaxActions = 100;

    public const int MaxTextLength = 500;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, TrySession> sessions = new(StringComparer.Ordinal);

    public DateTimeOffset ShiftStart => settings.ShiftStart;

    public DateTimeOffset ShiftEnd => settings.ShiftStart.AddHours(ShiftSimulator.ShiftHours);

    public TrySession Start(string patientId, int bedIndex, DateTimeOffset at)
    {
        var session = new TrySession(Guid.NewGuid().ToString("N"), patientId, bedIndex, at) { LastUsed = time.GetUtcNow() };
        sessions[session.Id] = session;
        while (sessions.Count > MaxSessions)
        {
            var oldest = sessions.Values.OrderBy(s => s.LastUsed).First();
            sessions.TryRemove(oldest.Id, out _);
        }

        return session;
    }

    /// <summary>The live session, or null when it is unknown or expired (an expired one is removed).</summary>
    public TrySession? Get(string id)
    {
        if (!sessions.TryGetValue(id, out var session))
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (now - session.LastUsed > Lifetime)
        {
            sessions.TryRemove(id, out _);
            return null;
        }

        session.LastUsed = now;
        return session;
    }

    public bool Remove(string id) => sessions.TryRemove(id, out _);

    /// <summary>Records the action, or returns the rule it breaks.</summary>
    public string? Add(TrySession session, TryAction action)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(action);

        if (!Enum.IsDefined(action.Kind) || (action.Tier is { } tier && !Enum.IsDefined(tier)))
        {
            return "Unknown action or tier.";
        }

        // Check and record under the session's lock, so two requests at once cannot both pass the order rule.
        lock (session.Gate)
        {
            return AddLocked(session, action);
        }
    }

    private string? AddLocked(TrySession session, TryAction action)
    {
        if (session.Actions.Count >= MaxActions)
        {
            return $"A Try session holds at most {MaxActions} actions. Reset to start again.";
        }

        if (action.Text?.Length > MaxTextLength)
        {
            return $"A note can be at most {MaxTextLength} characters.";
        }

        if (action.At < session.LastActionAt)
        {
            return $"An action must be at or after the last one ({session.LastActionAt:HH:mm}).";
        }

        if (action.At < ShiftStart || action.At >= ShiftEnd)
        {
            return "An action must be inside the shift.";
        }

        var records = Run(session).Records;
        var answered = TryCourse.AnsweredLatestCall(records, action.At);
        if (action.Kind is TryActionKind.Assessed or TryActionKind.Planned && answered is null)
        {
            return "The doctor you called has not arrived yet: an assessment or plan needs that doctor there.";
        }

        if (action.Kind == TryActionKind.Planned && !records.OfType<Assessed>().Any(a => a.CallId == answered && a.At <= action.At))
        {
            return "Record the assessment before the plan.";
        }

        session.Add(action);
        return null;
    }

    public TryResult Run(TrySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return TryCourse.Run(ShiftStart, settings.Seed, session.BedIndex, 1, session.StartedAt, session.Actions);
    }

    /// <summary>The bed index of a synthetic patient id ("synthetic-02" → 1), or null.</summary>
    public static int? BedIndexOf(string patientId) =>
        patientId.StartsWith("synthetic-", StringComparison.Ordinal)
        && int.TryParse(patientId["synthetic-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var bed)
        && bed is >= 1 and <= Ward.MaxBeds
            ? bed - 1
            : null;
}

/// <summary>The settings Try needs, taken once at startup.</summary>
public sealed record BatonOptionsSnapshot(DateTimeOffset ShiftStart, int Seed);

/// <summary>
/// The ward with one patient's Try course laid over it: that patient's observations, events and escalation records come
/// from the Try result; everyone else comes from the underlying source unchanged.
/// </summary>
public sealed class TryOverlaySource(IVitalSignsSource inner, TryResult result, DateTimeOffset shiftStart) : IVitalSignsSource
{
    private readonly string patientId = result.Bed.PatientId;
    private readonly Lazy<IReadOnlyList<Observation>> observations = new(() =>
        WardFhirBundle.Build(result.Bed).Entry.Select(e => e.Resource).OfType<Observation>().ToList());

    public Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken) => inner.GetPatientsAsync(cancellationToken);

    public Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        if (patientId != this.patientId)
        {
            return inner.GetObservationsAsync(patientId, until, cancellationToken);
        }

        IReadOnlyList<Observation> list = observations.Value.Where(o => WardService.TimeOf(o) is { } t && t <= until).ToList();
        return Task.FromResult(list);
    }

    public Task<PatientEvents> GetEventsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        if (patientId != this.patientId)
        {
            return inner.GetEventsAsync(patientId, until, cancellationToken);
        }

        IReadOnlyList<WardEvent> list = result.Bed.Patient.Events
            .Select(e => new WardEvent(ShiftSimulator.AtMinute(shiftStart, e.Hour), e.Kind, e.Text))
            .Where(e => e.At <= until)
            .OrderBy(e => e.At)
            .ToList();
        return Task.FromResult(new PatientEvents(true, list));
    }

    public Task<IReadOnlyList<EscalationRecord>> GetEscalationAsync(string patientId, DateTimeOffset until, string? language, CancellationToken cancellationToken)
    {
        if (patientId != this.patientId)
        {
            return inner.GetEscalationAsync(patientId, until, language, cancellationToken);
        }

        IReadOnlyList<EscalationRecord> list = result.Records
            .Where(r => r.At <= until)
            .Select(r => language == "tr" ? Translate(r) : r)
            .ToList();
        return Task.FromResult(list);
    }

    // Scenario texts kept from before the start have Turkish versions; what the user typed stays as typed.
    private static EscalationRecord Translate(EscalationRecord record) => record switch
    {
        ConcernRaised c => c with { Note = ScenarioTranslations.ToTurkish(c.Note) ?? c.Note },
        Assessed a => a with { Text = ScenarioTranslations.ToTurkish(a.Text) ?? a.Text },
        Planned p => p with { Text = ScenarioTranslations.ToTurkish(p.Text) ?? p.Text },
        _ => record,
    };
}
