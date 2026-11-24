using Baton.Api.Live;
using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Scenarios;

namespace Baton.Api.Shift;

public sealed record ShiftStartRequest(DateTimeOffset At, int Speed);

public sealed record ShiftSpeedRequest(int Speed);

public sealed record ShiftObservationRequest(string PatientId, VitalSet Vitals, DateTimeOffset? TakenAt);

public sealed record ShiftNotDoneRequest(string PatientId, NotDoneReason Reason, string? Note);

public sealed record ShiftActionRequest(string PatientId, ShiftActionKind Kind, string? Text, EscalationTier? Tier);

/// <summary>A set to score without storing. With a patient, their SpO2 scale and Scale 2 decision apply (at the shift's now).</summary>
public sealed record ScorePreviewRequest(VitalSet Vitals, SpO2Scale? Scale, string? PatientId = null, string? Shift = null);

/// <summary>A shift's clock as the UI needs it.</summary>
public sealed record ShiftState(string ShiftId, DateTimeOffset StartedAt, DateTimeOffset Now, int Speed, DateTimeOffset ShiftEnd);

/// <summary>What the bedside form shows after a save: the scored set, when the next one is due, and the chain.</summary>
public sealed record ShiftObservationResult(ScorePoint? Latest, DateTimeOffset? NextDue, EscalationState Escalation);

/// <summary>The bedside "Measure": the patient's vitals now (simulated) and the SpO2 scale the patient is on.</summary>
public sealed record MeasureResult(DateTimeOffset At, VitalSet Vitals, SpO2Scale Scale);

/// <summary>One call on the doctor's screen: the patient, the latest score and where the call stands.</summary>
public sealed record DoctorCall(
    string PatientId,
    string? Bed,
    string Name,
    ScorePoint? Latest,
    EscalationStep Step,
    CallState Call,
    int MinutesSinceCall,
    bool TargetPassed,
    EscalationTier? SuggestedNextTier);

/// <summary>The real ward flow (decisions 19–26): a private shift with a running clock, bedside entry and the call loop.</summary>
public static class ShiftEndpoints
{
    public static IResult Expired() =>
        Results.Problem("Shift ended or unknown. Start a new shift.", statusCode: StatusCodes.Status404NotFound, title: "Shift not found");

    private static IResult Refused(ShiftRefusal refusal) =>
        Results.Problem(
            refusal.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Entry not allowed",
            type: $"urn:baton:shift:{refusal.Code}",
            extensions: refusal.Field is null ? null : new Dictionary<string, object?> { ["field"] = refusal.Field });

    private static ShiftState StateOf(WardShift shift, WardShifts shifts) =>
        new(shift.Id, shift.StartedAt, shifts.Now(shift), shift.Speed, shifts.ShiftEnd);

    private static async Task<bool> Known(IVitalSignsSource source, string? patientId, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(patientId) && (await source.GetPatientsAsync(ct)).Any(p => p.Id == patientId);

    private static IResult UnknownPatient(string? patientId) =>
        Results.Problem($"Unknown patient '{patientId}'.", statusCode: StatusCodes.Status404NotFound);

    public static void Map(WebApplication app, IVitalSignsSource source)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/shift", (ShiftStartRequest body, WardShifts shifts) =>
        {
            if (source is not InMemoryVitalSignsSource)
            {
                return Results.Problem("A shift needs the in-memory data source.", statusCode: StatusCodes.Status409Conflict, title: "Shift unavailable");
            }

            if (shifts.Start(body.At, body.Speed) is not { } shift)
            {
                return Results.Problem(
                    $"A shift must start inside the scenario shift, at speed {string.Join(", ", WardShifts.Speeds)}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Created($"/api/shift/{shift.Id}", StateOf(shift, shifts));
        });

        app.MapGet("/api/shift/{id}", (string id, WardShifts shifts) =>
            shifts.Get(id) is { } shift ? Results.Ok(StateOf(shift, shifts)) : Expired());

        app.MapPut("/api/shift/{id}/speed", (string id, ShiftSpeedRequest body, WardShifts shifts, WardChangeFeed feed) =>
        {
            if (shifts.Get(id) is not { } shift)
            {
                return Expired();
            }

            if (!shifts.SetSpeed(shift, body.Speed))
            {
                return Results.Problem($"Speed must be {string.Join(", ", WardShifts.Speeds)}.", statusCode: StatusCodes.Status400BadRequest);
            }

            feed.Publish(new WardChange("Shift", null, shift.Id, shifts.Now(shift)));
            return Results.Ok(StateOf(shift, shifts));
        });

        app.MapDelete("/api/shift/{id}", (string id, WardShifts shifts, WardChangeFeed feed) =>
        {
            if (!shifts.Remove(id))
            {
                return Expired();
            }

            feed.Publish(new WardChange("Shift", null, id, DateTimeOffset.UtcNow));
            return Results.NoContent();
        });

        app.MapGet("/api/shift/{id}/patients/{patientId}/measure", async (string id, string patientId, WardShifts shifts, CancellationToken ct) =>
        {
            if (shifts.Get(id) is not { } shift)
            {
                return Expired();
            }

            if (!await Known(source, patientId, ct) || TrySessions.BedIndexOf(patientId) is not { } bedIndex)
            {
                return UnknownPatient(patientId);
            }

            var now = shifts.Now(shift);
            var arrivals = shift.RecordsOf(patientId).OfType<Arrived>().Select(a => a.At).ToList();
            var s = shifts.Settings;
            var (vitals, scale) = TryCourse.TrueVitalsAt(s.ShiftStart, s.Seed, bedIndex, shift.StartedAt, arrivals, now, s.TreatmentDelayHours);
            return Results.Ok(new MeasureResult(now, vitals, scale));
        });

        app.MapPost("/api/shift/{id}/observations", async (string id, ShiftObservationRequest body, WardShifts shifts, WardService ward, WardChangeFeed feed, CancellationToken ct) =>
        {
            if (shifts.Get(id) is not { } shift)
            {
                return Expired();
            }

            if (!await Known(source, body.PatientId, ct))
            {
                return UnknownPatient(body.PatientId);
            }

            if (body.Vitals is null)
            {
                return Results.Problem("vitals is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (shifts.Observe(shift, body.PatientId, body.Vitals, body.TakenAt) is { } refusal)
            {
                return Refused(refusal);
            }

            var now = shifts.Now(shift);
            feed.Publish(new WardChange("Shift", body.PatientId, shift.Id, now));
            var bed = (await ward.GetWardAsync(now, ct, null, new ShiftOverlaySource(source, shift))).Single(b => b.PatientId == body.PatientId);
            return Results.Ok(new ShiftObservationResult(bed.Latest, bed.NextDue, bed.Escalation));
        });

        app.MapPost("/api/shift/{id}/not-done", async (string id, ShiftNotDoneRequest body, WardShifts shifts, WardChangeFeed feed, CancellationToken ct) =>
        {
            if (shifts.Get(id) is not { } shift)
            {
                return Expired();
            }

            if (!await Known(source, body.PatientId, ct))
            {
                return UnknownPatient(body.PatientId);
            }

            if (shifts.NotDone(shift, body.PatientId, body.Reason, body.Note) is { } refusal)
            {
                return Refused(refusal);
            }

            feed.Publish(new WardChange("Shift", body.PatientId, shift.Id, shifts.Now(shift)));
            return Results.Ok(StateOf(shift, shifts));
        });

        app.MapPost("/api/shift/{id}/actions", async (string id, ShiftActionRequest body, WardShifts shifts, WardChangeFeed feed, CancellationToken ct) =>
        {
            if (shifts.Get(id) is not { } shift)
            {
                return Expired();
            }

            if (!await Known(source, body.PatientId, ct))
            {
                return UnknownPatient(body.PatientId);
            }

            var past = await new ShiftOverlaySource(source, shift).PastAsync(body.PatientId, ct);
            if (shifts.Act(shift, body.PatientId, body.Kind, body.Text, body.Tier, past) is { } refusal)
            {
                return Refused(refusal);
            }

            feed.Publish(new WardChange("Shift", body.PatientId, shift.Id, shifts.Now(shift)));
            return Results.Ok(StateOf(shift, shifts));
        });

        // The doctor's screen: every patient whose latest call has no plan yet, the most pressing first. A scenario call
        // answered before the shift began is history, not work: only calls made in the shift or still unanswered show.
        app.MapGet("/api/shift/{id}/calls", async (string id, string? lang, WardShifts shifts, WardService ward, CancellationToken ct) =>
        {
            if (shifts.Get(id) is not { } shift)
            {
                return Expired();
            }

            var now = shifts.Now(shift);
            var beds = await ward.GetWardAsync(now, ct, Language.From(lang), new ShiftOverlaySource(source, shift));
            var calls = beds
                .Where(b => b.Escalation.Calls.Count > 0 && b.Escalation.Calls[^1].PlannedAt is null
                    && (b.Escalation.Calls[^1].CalledAt >= shift.StartedAt || b.Escalation.Calls[^1].ArrivedAt is null))
                .Select(b =>
                {
                    var call = b.Escalation.Calls[^1];
                    return new DoctorCall(
                        b.PatientId,
                        b.Bed,
                        b.Name,
                        b.Latest,
                        b.Escalation.Step,
                        call,
                        (int)Math.Floor((now - call.CalledAt).TotalMinutes),
                        call.ArrivedAt is null && call.TargetAt is { } target && target < now,
                        b.Escalation.SuggestedNextTier);
                })
                .OrderBy(c => c.Call.ArrivedAt is null ? 0 : 1)
                .ThenBy(c => c.Call.TargetAt ?? DateTimeOffset.MaxValue)
                .ThenBy(c => c.Call.CalledAt)
                .ToList();
            return Results.Ok(new { now, calls });
        });

        // Score preview for the bedside form: the same engine, nothing stored (decision 25).
        app.MapPost("/api/news2", async (ScorePreviewRequest body, WardShifts shifts, CancellationToken ct) =>
        {
            if (body.Vitals is null)
            {
                return Results.Problem("vitals is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (VitalLimits.Check(body.Vitals) is { } bad)
            {
                return Refused(new ShiftRefusal("out-of-range", bad.Message, bad.Field));
            }

            var scale = body.Scale ?? SpO2Scale.Scale1;
            Scale2Authorisation? authorisation = null;
            if (!string.IsNullOrEmpty(body.PatientId))
            {
                if ((await source.GetPatientsAsync(ct)).FirstOrDefault(p => p.Id == body.PatientId) is not { } patient)
                {
                    return UnknownPatient(body.PatientId);
                }

                // RCP 2017: Scale 2 needs a recorded decision, in force at the moment the set is taken.
                var now = !string.IsNullOrEmpty(body.Shift) && shifts.Get(body.Shift) is { } shift ? shifts.Now(shift) : DateTimeOffset.MaxValue;
                scale = patient.SpO2Scale;
                authorisation = patient.Scale2Authorisation is { } decision && decision.AuthorisedAt <= now ? decision : null;
            }

            return Results.Ok(News2Calculator.Calculate(body.Vitals.ToNews2Input(scale, authorisation)));
        });
    }

    /// <summary>Turns a request's <c>shift</c> into the overlay and the ward-now to answer at, or into the problem.</summary>
    public static (IVitalSignsSource? Overlay, DateTimeOffset? Now, IResult? Problem) Resolve(string? shiftId, WardShifts shifts, IVitalSignsSource source, string? trySession = null)
    {
        if (string.IsNullOrEmpty(shiftId))
        {
            return (null, null, null);
        }

        if (!string.IsNullOrEmpty(trySession))
        {
            return (null, null, Results.Problem("Use either a Try session or a shift, not both.", statusCode: StatusCodes.Status400BadRequest));
        }

        return shifts.Get(shiftId) is { } shift
            ? (new ShiftOverlaySource(source, shift), shifts.Now(shift), null)
            : (null, null, Expired());
    }
}
