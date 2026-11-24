using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Baton.Api.Live;

/// <summary>
/// The FHIR R4 rest-hook Subscriptions that make the board live with HAPI (decision 17A): new or changed vital-sign
/// Observations, escalation Tasks, concern Flags and patient/family Communications call Baton back. Classic R4
/// criteria-based Subscriptions, not the R5 backport topics.
/// </summary>
public static class LiveSubscriptions
{
    public const string HookHeader = "X-Baton-Hook";

    private static readonly (string Id, string Criteria)[] Kinds =
    [
        ("baton-live-observation", "Observation?category=vital-signs"),
        ("baton-live-task", "Task?code=urn:baton:escalation|clinical-review"),
        ("baton-live-flag", "Flag?code=urn:baton:escalation|nurse-concern"),
        ("baton-live-communication", "Communication?category=urn:baton:escalation|patient-or-family-concern"),
    ];

    public static IReadOnlyList<Subscription> Build(string callbackUrl, string? secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(callbackUrl);
        return Kinds.Select(kind => new Subscription
        {
            Id = kind.Id,
            Status = Subscription.SubscriptionStatus.Requested,
            Reason = "Baton live board: refresh the ward when this changes",
            Criteria = kind.Criteria,
            Channel = new Subscription.ChannelComponent
            {
                Type = Subscription.SubscriptionChannelType.RestHook,
                Endpoint = callbackUrl,
                Payload = "application/fhir+json",
                Header = string.IsNullOrEmpty(secret) ? [] : [$"{HookHeader}: {secret}"],
            },
        }).ToList();
    }

    /// <summary>
    /// The patient a notified resource is about, read leniently from its JSON (<c>subject</c>, <c>for</c> or
    /// <c>patient</c>, or the first entry of a Bundle). Anything unreadable gives null: a notification never fails.
    /// </summary>
    public static string? PatientOf(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return PatientOf(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? PatientOf(JsonElement resource)
    {
        if (resource.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (resource.TryGetProperty("resourceType", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "Bundle"
            && resource.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            return entries.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("resource", out var r) ? PatientOf(r) : null)
                .FirstOrDefault(p => p is not null);
        }

        foreach (var name in new[] { "subject", "for", "patient" })
        {
            if (resource.TryGetProperty(name, out var reference) && reference.ValueKind == JsonValueKind.Object
                && reference.TryGetProperty("reference", out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } text && text.StartsWith("Patient/", StringComparison.Ordinal))
            {
                return text["Patient/".Length..];
            }
        }

        return null;
    }
}

/// <summary>
/// Writes the live Subscriptions to the FHIR server at start-up (HAPI source with <c>Baton:LiveCallbackUrl</c> set).
/// Retries while the server starts and logs each Subscription's status; it never stops the API.
/// </summary>
public sealed class LiveSubscriptionRegistrar(IOptions<BatonOptions> options, ILogger<LiveSubscriptionRegistrar> logger) : BackgroundService
{
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(10);
    public const int Attempts = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.SourceName != BatonOptions.Hapi || string.IsNullOrEmpty(settings.LiveCallbackUrl))
        {
            return;
        }

        for (var attempt = 1; attempt <= Attempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var client = HapiVitalSignsSource.CreateClient(settings.FhirBaseUrl);
                foreach (var subscription in LiveSubscriptions.Build(settings.LiveCallbackUrl, settings.HookSecret))
                {
                    await client.UpdateAsync(subscription, ct: stoppingToken);
                }

                foreach (var subscription in LiveSubscriptions.Build(settings.LiveCallbackUrl, settings.HookSecret))
                {
                    var stored = await client.ReadAsync<Subscription>($"Subscription/{subscription.Id}", ct: stoppingToken);
                    if (stored?.Status == Subscription.SubscriptionStatus.Active)
                    {
                        logger.LogInformation("Live subscription {Id} is active", subscription.Id);
                    }
                    else
                    {
                        logger.LogWarning("Live subscription {Id} is {Status}; is resthook_enabled set on the FHIR server?", subscription.Id, stored?.Status);
                    }
                }

                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Never fatal: a live board without push still works by moving the clock.
                logger.LogWarning("Live subscriptions not written (attempt {Attempt}/{Attempts}): {Message}", attempt, Attempts, ex.Message);
                try
                {
                    await Task.Delay(RetryEvery, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Gave up writing live subscriptions; the board will not refresh by itself from the FHIR server.");
        }
    }
}
