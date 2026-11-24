using System.Collections.Concurrent;
using System.Globalization;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Baton.Api.Handover;

/// <summary>A confirmed handover: the stored document and the handover moment (scenario time).</summary>
public sealed record ConfirmedHandover(string DocumentId, DateTimeOffset At);

/// <summary>Where confirmed handover documents are kept.</summary>
public interface IHandoverStore
{
    /// <summary>The patient's latest confirmed handover at or before <paramref name="atOrBefore"/>.</summary>
    Task<ConfirmedHandover?> LatestAsync(string patientId, DateTimeOffset atOrBefore, CancellationToken cancellationToken);

    /// <summary>Stores the document and its Provenance; false when a document with the same id already exists.</summary>
    Task<bool> TryAddAsync(string patientId, Bundle document, Provenance provenance, CancellationToken cancellationToken);
}

/// <summary>Handover documents in the API's memory, for the process lifetime (the in-memory ward).</summary>
public sealed class InMemoryHandoverStore : IHandoverStore
{
    private readonly ConcurrentDictionary<string, Stored> documents = new(StringComparer.Ordinal);

    private sealed record Stored(string PatientId, DateTimeOffset At, Bundle Document, Provenance Provenance);

    public Task<ConfirmedHandover?> LatestAsync(string patientId, DateTimeOffset atOrBefore, CancellationToken cancellationToken)
    {
        var latest = documents.Values
            .Where(d => d.PatientId == patientId && d.At <= atOrBefore)
            .OrderBy(d => d.At)
            .LastOrDefault();
        return Task.FromResult(latest is null ? null : new ConfirmedHandover(latest.Document.Id!, latest.At));
    }

    public Task<bool> TryAddAsync(string patientId, Bundle document, Provenance provenance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var at = document.Timestamp ?? throw new ArgumentException("A handover document needs a timestamp.", nameof(document));
        return Task.FromResult(documents.TryAdd(document.Id!, new Stored(patientId, at, document, provenance)));
    }
}

/// <summary>
/// Handover documents on a FHIR server: the Bundle is written with its own id (PUT), its Provenance beside it. The
/// latest per patient is found by the Bundle identifier (system <see cref="HandoverDocument.IdentifierSystem"/>, value
/// <c>handover-{patientId}-{yyyyMMddHHmm}</c>).
/// </summary>
public sealed class HapiHandoverStore(FhirClient client) : IHandoverStore
{
    public async Task<ConfirmedHandover?> LatestAsync(string patientId, DateTimeOffset atOrBefore, CancellationToken cancellationToken)
    {
        var prefix = $"handover-{patientId}-";
        var query = new SearchParams().Where($"identifier={HandoverDocument.IdentifierSystem}|").LimitTo(200);
        query.Elements.Add("identifier");
        query.Elements.Add("timestamp");
        var found = new List<ConfirmedHandover>();
        var page = await client.SearchAsync<Bundle>(query, cancellationToken);
        while (page is not null)
        {
            found.AddRange(page.Entry.Select(e => e.Resource).OfType<Bundle>()
                .Where(b => b.Identifier?.Value?.StartsWith(prefix, StringComparison.Ordinal) == true && b.Timestamp is not null)
                .Select(b => new ConfirmedHandover(b.Identifier!.Value!, b.Timestamp!.Value)));
            page = page.NextLink is null ? null : await client.ContinueAsync(page, ct: cancellationToken);
        }

        return found.Where(h => h.At <= atOrBefore).OrderBy(h => h.At).LastOrDefault();
    }

    public async Task<bool> TryAddAsync(string patientId, Bundle document, Provenance provenance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(provenance);
        if (await ExistsAsync($"Bundle/{document.Id}", cancellationToken))
        {
            return false;
        }

        await client.UpdateAsync(document, ct: cancellationToken);
        await client.UpdateAsync(provenance, ct: cancellationToken);
        return true;
    }

    private async Task<bool> ExistsAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await client.ReadAsync<Bundle>(reference, ct: cancellationToken) is not null;
        }
        catch (FhirOperationException ex) when (ex.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            return false;
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"HapiHandoverStore({client.Endpoint})");
}
