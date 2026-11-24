using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Baton.Api;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Options;

// Baton API: scores the synthetic ward from FHIR. Not a medical device; synthetic data only.
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BatonOptions>(builder.Configuration.GetSection("Baton"));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<IVitalSignsSource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<BatonOptions>>().Value;
    return options.SourceName switch
    {
        BatonOptions.Hapi => new HapiVitalSignsSource(HapiVitalSignsSource.CreateClient(options.FhirBaseUrl)),
        BatonOptions.InMemory => new InMemoryVitalSignsSource(options.ShiftStart, options.Seed, options.TreatmentDelayHours, options.Beds),
        _ => throw new InvalidOperationException($"Unknown Baton:Source '{options.Source}'. Use InMemory or Hapi."),
    };
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<BatonOptions>>().Value;
    return new Baton.Core.Escalation.EscalationOptions
    {
        MediumTargetMinutes = options.MediumTargetMinutes,
        HighTargetMinutes = options.HighTargetMinutes,
    };
});
builder.Services.AddSingleton<WardService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<BatonOptions>>().Value;
    return new BatonOptionsSnapshot(options.ShiftStart, options.Seed);
});
builder.Services.AddSingleton<TrySessions>();
builder.Services.AddSingleton<Baton.Api.Live.WardChangeFeed>();
builder.Services.AddHostedService<Baton.Api.Live.LiveSubscriptionRegistrar>();
builder.Services.AddSingleton<Baton.Api.Handover.IHandoverStore>(sp =>
{
    var options = sp.GetRequiredService<IOptions<BatonOptions>>().Value;
    return options.SourceName == BatonOptions.Hapi
        ? new Baton.Api.Handover.HapiHandoverStore(HapiVitalSignsSource.CreateClient(options.FhirBaseUrl))
        : new Baton.Api.Handover.InMemoryHandoverStore();
});
builder.Services.AddSingleton(sp => new Baton.Api.Handover.HandoverService(
    sp.GetRequiredService<WardService>(),
    sp.GetRequiredService<IVitalSignsSource>(),
    sp.GetRequiredService<Baton.Api.Handover.IHandoverStore>(),
    sp.GetRequiredService<IOptions<BatonOptions>>().Value.ShiftStart));

var app = builder.Build();

// Check the settings and resolve the source now so a wrong setting fails at startup, not on every request.
var settings = app.Services.GetRequiredService<IOptions<BatonOptions>>().Value;
if (settings.Beds is < 1 or > Baton.Scenarios.Ward.MaxBeds)
{
    throw new InvalidOperationException($"Baton:Beds must be between 1 and {Baton.Scenarios.Ward.MaxBeds}.");
}

if (settings.TreatmentDelayHours < 0)
{
    throw new InvalidOperationException("Baton:TreatmentDelayHours must not be negative.");
}

var source = app.Services.GetRequiredService<IVitalSignsSource>();
app.Logger.LogInformation("Baton source: {Source} {Url}", settings.SourceName, settings.SourceName == BatonOptions.Hapi ? settings.FhirBaseUrl : "(generated in memory)");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    source = settings.SourceName,
    tryAvailable = source is InMemoryVitalSignsSource,
    fhirBaseUrl = settings.SourceName == BatonOptions.Hapi ? settings.FhirBaseUrl : null,
    note = "Synthetic data. Not a medical device.",
}));

app.MapGet("/api/ward", async (string? at, string? lang, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "try")] string? trySession, WardService ward, TrySessions tries, CancellationToken ct) =>
{
    if (!TimeQuery.TryParse(at, settings.DefaultAt, out var moment, out var error))
    {
        return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    var (overlay, tryProblem) = TryOverlay.Resolve(trySession, tries, source);
    if (tryProblem is not null)
    {
        return tryProblem;
    }

    moment = moment.ToOffset(settings.ShiftStart.Offset); // answer in ward time, whatever offset was sent

    return await FhirServerProblem.Guard(async () =>
        Results.Ok(new WardResponse(moment, settings.SourceName, settings.ShiftStart, settings.ShiftStart.AddHours(24), await ward.GetWardAsync(moment, ct, Language.From(lang), overlay))));
});

app.MapGet("/api/patients/{id}", async (string id, string? at, string? lang, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "try")] string? trySession, WardService ward, TrySessions tries, CancellationToken ct) =>
{
    if (!TimeQuery.TryParse(at, settings.DefaultAt, out var moment, out var error))
    {
        return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    var (overlay, tryProblem) = TryOverlay.Resolve(trySession, tries, source);
    if (tryProblem is not null)
    {
        return tryProblem;
    }

    moment = moment.ToOffset(settings.ShiftStart.Offset); // answer in ward time, whatever offset was sent

    return await FhirServerProblem.Guard(async () =>
        await ward.GetPatientAsync(id, moment, ct, Language.From(lang), overlay) is { } series ? Results.Ok(series) : Results.NotFound());
});

app.MapPost("/api/try", async (TryStartRequest body, TrySessions tries, Baton.Api.Live.WardChangeFeed feed, CancellationToken ct) =>
{
    if (source is not InMemoryVitalSignsSource)
    {
        return Results.Problem("Try mode needs the in-memory data source.", statusCode: StatusCodes.Status409Conflict, title: "Try mode unavailable");
    }

    if (string.IsNullOrWhiteSpace(body.PatientId))
    {
        return Results.Problem("patientId is required.", statusCode: StatusCodes.Status400BadRequest);
    }

    var patients = await source.GetPatientsAsync(ct);
    if (TrySessions.BedIndexOf(body.PatientId) is not { } bedIndex || patients.All(p => p.Id != body.PatientId))
    {
        return Results.Problem($"Unknown patient '{body.PatientId}'.", statusCode: StatusCodes.Status404NotFound);
    }

    var at = body.At.ToOffset(settings.ShiftStart.Offset);
    if (at < tries.ShiftStart || at >= tries.ShiftEnd)
    {
        return Results.Problem("Try must start inside the shift.", statusCode: StatusCodes.Status400BadRequest);
    }

    var session = tries.Start(body.PatientId, bedIndex, at);
    feed.Publish(new Baton.Api.Live.WardChange("Try", session.PatientId, session.Id, at));
    return Results.Created($"/api/try/{session.Id}", new { sessionId = session.Id, patientId = session.PatientId, startedAt = session.StartedAt });
});

app.MapPost("/api/try/{id}/actions", (string id, TryActionRequest body, TrySessions tries, Baton.Api.Live.WardChangeFeed feed) =>
{
    if (tries.Get(id) is not { } session)
    {
        return TryOverlay.Expired();
    }

    var action = new Baton.Scenarios.TryAction(body.Kind, body.At.ToOffset(settings.ShiftStart.Offset), body.Text, body.Tier);
    if (tries.Add(session, action) is { } broken)
    {
        return Results.Problem(broken, statusCode: StatusCodes.Status400BadRequest, title: "Action not allowed");
    }

    feed.Publish(new Baton.Api.Live.WardChange("Try", session.PatientId, session.Id, action.At));
    return Results.Ok(new { lastActionAt = session.LastActionAt });
});

app.MapDelete("/api/try/{id}", (string id, TrySessions tries, Baton.Api.Live.WardChangeFeed feed) =>
{
    var session = tries.Get(id);
    if (!tries.Remove(id))
    {
        return TryOverlay.Expired();
    }

    feed.Publish(new Baton.Api.Live.WardChange("Try", session?.PatientId, id, DateTimeOffset.UtcNow));
    return Results.NoContent();
});

// The FHIR server's rest-hook (decision 17A): a subscribed resource changed. Only "this patient changed" is published;
// whatever the body is (resource, empty, unreadable), the answer is 200, unless the shared secret does not match.
app.MapPost("/api/fhir/notify", async (HttpRequest request, Baton.Api.Live.WardChangeFeed feed, CancellationToken ct) =>
{
    if (!string.IsNullOrEmpty(settings.HookSecret)
        && request.Headers[Baton.Api.Live.LiveSubscriptions.HookHeader].ToString() != settings.HookSecret)
    {
        return Results.Problem("Hook secret mismatch.", statusCode: StatusCodes.Status401Unauthorized);
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct);
    feed.Publish(new Baton.Api.Live.WardChange("Fhir", Baton.Api.Live.LiveSubscriptions.PatientOf(body), null, DateTimeOffset.UtcNow));
    return Results.Ok();
});

// Live board: "something changed, refetch" as Server-Sent Events (decision 18A).
app.MapGet("/api/live", (Baton.Api.Live.WardChangeFeed feed, CancellationToken ct) =>
    TypedResults.ServerSentEvents(feed.Stream(TimeSpan.FromSeconds(25), ct)));

app.MapGet("/api/handover", async (string? at, string? lang, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "try")] string? trySession, Baton.Api.Handover.HandoverService handover, TrySessions tries, CancellationToken ct) =>
{
    if (!TimeQuery.TryParse(at, settings.DefaultAt, out var moment, out var error))
    {
        return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    var (overlay, tryProblem) = TryOverlay.Resolve(trySession, tries, source);
    if (tryProblem is not null)
    {
        return tryProblem;
    }

    moment = moment.ToOffset(settings.ShiftStart.Offset);

    return await FhirServerProblem.Guard(async () =>
        Results.Ok(await handover.GetAsync(moment, Language.From(lang), overlay, canConfirm: overlay is null, ct)));
});

// "Taken over": stores the patient's SBAR at that minute as a FHIR document. Never in Try mode (a rehearsal).
app.MapPost("/api/handover/{patientId}", async (string patientId, HandoverConfirmRequest body, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "try")] string? trySession, Baton.Api.Handover.HandoverService handover, Baton.Api.Live.WardChangeFeed feed, CancellationToken ct) =>
{
    if (!string.IsNullOrEmpty(trySession))
    {
        return Results.Problem("A Try session is a rehearsal: its handover is not stored.", statusCode: StatusCodes.Status409Conflict, title: "Handover not stored in Try");
    }

    var local = body.At.ToOffset(settings.ShiftStart.Offset);
    var minute = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset);

    return await FhirServerProblem.Guard(async () =>
    {
        var (outcome, confirmed) = await handover.ConfirmAsync(patientId, minute, Language.From(body.Lang), ct);
        IResult Created()
        {
            feed.Publish(new Baton.Api.Live.WardChange("Handover", patientId, null, confirmed!.At));
            return Results.Created($"/api/handover/{patientId}", new { documentId = confirmed.DocumentId, at = confirmed.At });
        }

        return outcome switch
        {
            Baton.Api.Handover.ConfirmOutcome.Created => Created(),
            Baton.Api.Handover.ConfirmOutcome.UnknownPatient => Results.Problem($"Unknown patient '{patientId}'.", statusCode: StatusCodes.Status404NotFound),
            _ => Results.Problem("This patient was already taken over at this minute.", statusCode: StatusCodes.Status409Conflict, title: "Already taken over"),
        };
    });
});

GC.KeepAlive(source);
app.Run();

public sealed record WardResponse(DateTimeOffset At, string Source, DateTimeOffset ShiftStart, DateTimeOffset ShiftEnd, IReadOnlyList<BedSummary> Beds);

public sealed record HandoverConfirmRequest(DateTimeOffset At, string? Lang);

public sealed record TryStartRequest(string PatientId, DateTimeOffset At);

public sealed record TryActionRequest(Baton.Scenarios.TryActionKind Kind, DateTimeOffset At, string? Text, Baton.Core.Escalation.EscalationTier? Tier);

/// <summary>Turns a request's <c>try</c> session into the ward overlay, or into the problem to answer with.</summary>
public static class TryOverlay
{
    public static IResult Expired() =>
        Results.Problem("Try session expired or unknown. Start Try again.", statusCode: StatusCodes.Status404NotFound, title: "Try session expired");

    public static (IVitalSignsSource? Overlay, IResult? Problem) Resolve(string? sessionId, TrySessions tries, IVitalSignsSource source)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return (null, null);
        }

        if (tries.Get(sessionId) is not { } session)
        {
            return (null, Expired());
        }

        return (new TryOverlaySource(source, tries.Run(session), tries.ShiftStart), null);
    }
}

/// <summary>The data language a request asks for: "tr" (Turkish translations) or null (the original English).</summary>
public static class Language
{
    public static string? From(string? lang) => string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase) ? "tr" : null;
}

/// <summary>Turns a FHIR server that cannot be reached into 503 problem details instead of a raw 500.</summary>
public static class FhirServerProblem
{
    public static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or FhirOperationException)
        {
            return Results.Problem(
                title: "FHIR server unreachable",
                detail: "The FHIR server did not answer. Check that it is running (docker compose up -d).",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

/// <summary>Parses the <c>at</c> query parameter. It must carry an offset; an unencoded '+' arrives as a space.</summary>
public static partial class TimeQuery
{
    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex OffsetSuffix();

    public static bool TryParse(string? raw, DateTimeOffset defaultAt, out DateTimeOffset at, out string? error)
    {
        at = defaultAt;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim().Replace(' ', '+');
        if (!OffsetSuffix().IsMatch(text))
        {
            error = "'at' must include a time zone offset, for example 2026-11-23T23:30:00+03:00.";
            return false;
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
        {
            error = $"'at' is not a valid ISO 8601 date and time: '{raw}'.";
            at = defaultAt;
            return false;
        }

        return true;
    }
}

/// <summary>Settings under "Baton" in appsettings.json or environment variables (Baton__Source=Hapi).</summary>
public sealed class BatonOptions
{
    public const string InMemory = "InMemory";
    public const string Hapi = "Hapi";

    /// <summary>InMemory (generated, no server) or Hapi (read from <see cref="FhirBaseUrl"/>). Case-insensitive.</summary>
    public string Source { get; set; } = InMemory;

    public string SourceName =>
        string.Equals(Source, Hapi, StringComparison.OrdinalIgnoreCase) ? Hapi
        : string.Equals(Source, InMemory, StringComparison.OrdinalIgnoreCase) ? InMemory
        : Source;

    public string FhirBaseUrl { get; set; } = "http://localhost:8080/fhir";

    public int Seed { get; set; } = 42;

    /// <summary>In-memory source: how many beds to generate (1–30; 8 story beds by default).</summary>
    public int Beds { get; set; } = Baton.Scenarios.Ward.DefaultBeds;

    /// <summary>Response target for a score of 5–6 or a single parameter scoring 3, in minutes (RCP NEWS2: 60).</summary>
    public int MediumTargetMinutes { get; set; } = 60;

    /// <summary>Response target for a score of 7 or more, in minutes (RCP NEWS2: 30).</summary>
    public int HighTargetMinutes { get; set; } = 30;

    /// <summary>In-memory source: hours by which treatment starts late (decision 2B). The FHIR loader has --delay.</summary>
    public double TreatmentDelayHours { get; set; }

    public DateTimeOffset ShiftStart { get; set; } = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    /// <summary>
    /// HAPI source: where the FHIR server calls back when subscribed resources change (live board). Empty: no
    /// Subscriptions are written. From inside Docker the host is http://host.docker.internal:5188/api/fhir/notify.
    /// </summary>
    public string? LiveCallbackUrl { get; set; }

    /// <summary>Shared secret the FHIR server sends in the X-Baton-Hook header; when set, other calls get 401.</summary>
    public string? HookSecret { get; set; }

    /// <summary>The synthetic shift is fixed in time, so "now" defaults to a moment inside it.</summary>
    public DateTimeOffset DefaultAt { get; set; } = new(2026, 11, 23, 23, 30, 0, TimeSpan.FromHours(3));
}

public partial class Program;
