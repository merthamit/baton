using System.Collections.Concurrent;
using Baton.Core.Escalation;
using Baton.Scenarios;

namespace Baton.Api.Shift;

/// <summary>What a shift user does, as nurse (concern, call) or as the called clinician (the rest).</summary>
public enum ShiftActionKind
{
    Concern,
    PatientOrFamilyConcern,
    Call,
    Acknowledge,
    Arrive,
    Assess,
    Plan,
}

/// <summary>One set of observations entered at the bedside: taken at <see cref="At"/>, entered at <see cref="RecordedAt"/>.</summary>
public sealed record ShiftObservation(string PatientId, DateTimeOffset At, DateTimeOffset RecordedAt, VitalSet Vitals, int Number);

/// <summary>Why a shift entry is refused: a stable code the UI translates (urn:baton:shift:{Code}), English, and the field.</summary>
public sealed record ShiftRefusal(string Code, string Message, string? Field = null);

/// <summary>
/// One visitor's private copy of the ward (decision 19). Before <see cref="StartedAt"/> the scenario stands; from it,
/// only what the visitor enters happens. The ward clock runs at <see cref="Speed"/> × real time (decision 20).
/// </summary>
public sealed class WardShift(string id, DateTimeOffset startedAt, DateTimeOffset realStart, int speed)
{
    private readonly List<ShiftObservation> observations = [];
    private readonly List<(string PatientId, EscalationRecord Record)> records = [];
    private long lastUsedTicks;

    public string Id { get; } = id;

    public DateTimeOffset StartedAt { get; } = startedAt;

    public object Gate { get; } = new();

    /// <summary>The clock anchor: at real time <see cref="AnchorReal"/> the ward clock read <see cref="AnchorWard"/>.</summary>
    public DateTimeOffset AnchorReal { get; private set; } = realStart;

    public DateTimeOffset AnchorWard { get; private set; } = startedAt;

    public int Speed { get; private set; } = speed;

    public DateTimeOffset LastUsed
    {
        get => new(Interlocked.Read(ref lastUsedTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref lastUsedTicks, value.UtcTicks);
    }

    public int EntryCount
    {
        get
        {
            lock (Gate)
            {
                return observations.Count + records.Count;
            }
        }
    }

    public IReadOnlyList<ShiftObservation> Observations
    {
        get
        {
            lock (Gate)
            {
                return [.. observations];
            }
        }
    }

    public IReadOnlyList<EscalationRecord> RecordsOf(string patientId)
    {
        lock (Gate)
        {
            return records.Where(r => r.PatientId == patientId).Select(r => r.Record).ToList();
        }
    }

    /// <summary>Every record of the shift with its patient, oldest first.</summary>
    public IReadOnlyList<(string PatientId, EscalationRecord Record)> AllRecords
    {
        get
        {
            lock (Gate)
            {
                return [.. records];
            }
        }
    }

    internal void SetClock(DateTimeOffset real, DateTimeOffset ward, int speed)
    {
        AnchorReal = real;
        AnchorWard = ward;
        Speed = speed;
    }

    internal void Add(ShiftObservation observation) => observations.Add(observation);

    internal void Add(string patientId, EscalationRecord record) => records.Add((patientId, record));
}

/// <summary>
/// Shifts kept in API memory, like Try sessions: at most 50, dropped four hours after their last use (least recently used
/// first when full). Every entry is stamped by the server with the shift's ward-now.
/// </summary>
public sealed class WardShifts(TimeProvider time, BatonOptionsSnapshot settings)
{
    public const int MaxShifts = 50;
    public const int MaxEntries = 1000;
    public const int MaxTextLength = 500;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(4);
    public static readonly IReadOnlyList<int> Speeds = [1, 10, 60];

    private readonly ConcurrentDictionary<string, WardShift> shifts = new(StringComparer.Ordinal);

    public DateTimeOffset ShiftStart => settings.ShiftStart;

    public DateTimeOffset ShiftEnd => settings.ShiftStart.AddHours(ShiftSimulator.ShiftHours);

    public BatonOptionsSnapshot Settings => settings;

    /// <summary>A new shift from ward moment <paramref name="at"/>, or null when the moment or speed is not allowed.</summary>
    public WardShift? Start(DateTimeOffset at, int speed)
    {
        if (!Speeds.Contains(speed) || at < ShiftStart || at >= ShiftEnd)
        {
            return null;
        }

        var minute = at.ToOffset(ShiftStart.Offset);
        minute = new DateTimeOffset(minute.Year, minute.Month, minute.Day, minute.Hour, minute.Minute, 0, minute.Offset);
        var now = time.GetUtcNow();
        var shift = new WardShift(Guid.NewGuid().ToString("N"), minute, now, speed) { LastUsed = now };
        shifts[shift.Id] = shift;
        while (shifts.Count > MaxShifts)
        {
            var oldest = shifts.Values.OrderBy(s => s.LastUsed).First();
            shifts.TryRemove(oldest.Id, out _);
        }

        return shift;
    }

    public WardShift? Get(string id)
    {
        if (!shifts.TryGetValue(id, out var shift))
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (now - shift.LastUsed > Lifetime)
        {
            shifts.TryRemove(id, out _);
            return null;
        }

        shift.LastUsed = now;
        return shift;
    }

    public bool Remove(string id) => shifts.TryRemove(id, out _);

    /// <summary>The shift's ward-now, to the second, never past the end of the scenario shift.</summary>
    public DateTimeOffset Now(WardShift shift)
    {
        ArgumentNullException.ThrowIfNull(shift);
        lock (shift.Gate)
        {
            return NowLocked(shift);
        }
    }

    private DateTimeOffset NowLocked(WardShift shift)
    {
        var elapsed = (time.GetUtcNow() - shift.AnchorReal) * shift.Speed;
        var now = shift.AnchorWard + (elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
        now = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), now.Offset);
        return now > ShiftEnd ? ShiftEnd : now;
    }

    public bool SetSpeed(WardShift shift, int speed)
    {
        ArgumentNullException.ThrowIfNull(shift);
        if (!Speeds.Contains(speed))
        {
            return false;
        }

        lock (shift.Gate)
        {
            shift.SetClock(time.GetUtcNow(), NowLocked(shift), speed);
        }

        return true;
    }

    /// <summary>Records a bedside set, taken now or (late entry) at <paramref name="takenAt"/>, or returns the rule it breaks.</summary>
    public ShiftRefusal? Observe(WardShift shift, string patientId, VitalSet vitals, DateTimeOffset? takenAt)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(vitals);
        if (VitalLimits.Check(vitals) is { } bad)
        {
            return new("out-of-range", bad.Message, bad.Field);
        }

        lock (shift.Gate)
        {
            var now = NowLocked(shift);
            if (Common(shift, now, null) is { } refusal)
            {
                return refusal;
            }

            var at = takenAt?.ToOffset(ShiftStart.Offset) ?? now;
            if (at > now || at < shift.StartedAt)
            {
                return new("taken-time", "The time taken must be between the start of this shift and now.", "takenAt");
            }

            shift.Add(new ShiftObservation(patientId, at, now, vitals, shift.Observations.Count + 1));
            return null;
        }
    }

    public ShiftRefusal? NotDone(WardShift shift, string patientId, NotDoneReason reason, string? note)
    {
        ArgumentNullException.ThrowIfNull(shift);
        if (!Enum.IsDefined(reason))
        {
            return new("unknown", "Unknown reason.", "reason");
        }

        lock (shift.Gate)
        {
            var now = NowLocked(shift);
            if (Common(shift, now, note) is { } refusal)
            {
                return refusal;
            }

            shift.Add(patientId, new ObservationNotDone(now, TryCourse.You, reason, string.IsNullOrWhiteSpace(note) ? null : note.Trim()));
            return null;
        }
    }

    /// <summary>Records a nurse or clinician step on the patient's latest call, or returns the rule it breaks.</summary>
    /// <remarks><paramref name="past"/>: the patient's scenario records; those before the start count (an open call there can be answered).</remarks>
    public ShiftRefusal? Act(WardShift shift, string patientId, ShiftActionKind kind, string? text, EscalationTier? tier, IReadOnlyList<EscalationRecord>? past = null)
    {
        ArgumentNullException.ThrowIfNull(shift);
        if (!Enum.IsDefined(kind) || (tier is { } t && !Enum.IsDefined(t)))
        {
            return new("unknown", "Unknown action or tier.");
        }

        lock (shift.Gate)
        {
            var now = NowLocked(shift);
            if (Common(shift, now, text) is { } refusal)
            {
                return refusal;
            }

            var own = shift.RecordsOf(patientId);
            var mine = (past ?? []).Where(r => r.At < shift.StartedAt).Concat(own).ToList();
            var latest = mine.OfType<Called>().LastOrDefault();
            bool Has<T>(Func<T, bool> match) => mine.OfType<T>().Any(match);
            var arrived = latest is not null && Has<Arrived>(a => a.CallId == latest.CallId);
            var clinician = latest is null ? "" : ClinicianFor(latest.Tier);
            var note = text?.Trim() ?? "";

            switch (kind)
            {
                case ShiftActionKind.Concern:
                    shift.Add(patientId, new ConcernRaised(now, TryCourse.You, note));
                    return null;
                case ShiftActionKind.PatientOrFamilyConcern:
                    shift.Add(patientId, new PatientOrFamilyConcern(now, TryCourse.Family, note));
                    return null;
                case ShiftActionKind.Call:
                    var callId = $"shift-{shift.Id[..8]}-{patientId}-{own.OfType<Called>().Count() + 1}";
                    shift.Add(patientId, new Called(now, TryCourse.You, callId, tier ?? EscalationTier.OnCallDoctor));
                    return null;
                case ShiftActionKind.Acknowledge:
                    if (latest is null || arrived)
                    {
                        return new("no-open-call", "There is no open call to acknowledge.");
                    }

                    if (Has<Acknowledged>(a => a.CallId == latest.CallId))
                    {
                        return new("already-acknowledged", "This call is already acknowledged.");
                    }

                    shift.Add(patientId, new Acknowledged(now, clinician, latest.CallId));
                    return null;
                case ShiftActionKind.Arrive:
                    if (latest is null || arrived)
                    {
                        return new("no-open-call", "There is no open call to arrive for.");
                    }

                    if (!Has<Acknowledged>(a => a.CallId == latest.CallId))
                    {
                        shift.Add(patientId, new Acknowledged(now, clinician, latest.CallId)); // arriving answers the call
                    }

                    shift.Add(patientId, new Arrived(now, clinician, latest.CallId, Simulated: false));
                    return null;
                case ShiftActionKind.Assess:
                    if (latest is null || !arrived)
                    {
                        return new("no-arrival", "The called clinician has not arrived yet.");
                    }

                    shift.Add(patientId, new Assessed(now, clinician, latest.CallId, note));
                    return null;
                default: // Plan
                    if (latest is null || !arrived)
                    {
                        return new("no-arrival", "The called clinician has not arrived yet.");
                    }

                    if (!Has<Assessed>(a => a.CallId == latest.CallId))
                    {
                        return new("assessment-first", "Record the assessment before the plan.");
                    }

                    shift.Add(patientId, new Planned(now, clinician, latest.CallId, note));
                    return null;
            }
        }
    }

    private ShiftRefusal? Common(WardShift shift, DateTimeOffset now, string? text)
    {
        if (now >= ShiftEnd)
        {
            return new("shift-over", "The shift has ended. Start a new one.");
        }

        if (shift.EntryCount >= MaxEntries)
        {
            return new("too-many", $"A shift holds at most {MaxEntries} entries. Start a new one.");
        }

        return text?.Length > MaxTextLength ? new("note-too-long", $"A note can be at most {MaxTextLength} characters.", "text") : null;
    }

    public static string ClinicianFor(EscalationTier tier) => tier switch
    {
        EscalationTier.SeniorDoctor => ScenarioEscalation.SeniorDoctor,
        EscalationTier.CriticalCare => ScenarioEscalation.CriticalCareTeam,
        _ => ScenarioEscalation.OnCallDoctor,
    };
}
