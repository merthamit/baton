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
    fhirBaseUrl = settings.SourceName == BatonOptions.Hapi ? settings.FhirBaseUrl : null,
    note = "Synthetic data. Not a medical device.",
}));

app.MapGet("/api/ward", async (string? at, WardService ward, CancellationToken ct) =>
{
    if (!TimeQuery.TryParse(at, settings.DefaultAt, out var moment, out var error))
    {
        return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    moment = moment.ToOffset(settings.ShiftStart.Offset); // answer in ward time, whatever offset was sent

    return await FhirServerProblem.Guard(async () =>
        Results.Ok(new WardResponse(moment, settings.SourceName, settings.ShiftStart, settings.ShiftStart.AddHours(24), await ward.GetWardAsync(moment, ct))));
});

app.MapGet("/api/patients/{id}", async (string id, string? at, WardService ward, CancellationToken ct) =>
{
    if (!TimeQuery.TryParse(at, settings.DefaultAt, out var moment, out var error))
    {
        return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    moment = moment.ToOffset(settings.ShiftStart.Offset); // answer in ward time, whatever offset was sent

    return await FhirServerProblem.Guard(async () =>
        await ward.GetPatientAsync(id, moment, ct) is { } series ? Results.Ok(series) : Results.NotFound());
});

GC.KeepAlive(source);
app.Run();

public sealed record WardResponse(DateTimeOffset At, string Source, DateTimeOffset ShiftStart, DateTimeOffset ShiftEnd, IReadOnlyList<BedSummary> Beds);

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

    /// <summary>The synthetic shift is fixed in time, so "now" defaults to a moment inside it.</summary>
    public DateTimeOffset DefaultAt { get; set; } = new(2026, 11, 23, 23, 30, 0, TimeSpan.FromHours(3));
}

public partial class Program;
