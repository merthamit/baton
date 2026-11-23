using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Baton.Scenarios;

/// <summary>
/// Loads the synthetic ward into a FHIR server. For each bed it first deletes the patient's existing
/// Observations, so loading again with another seed or delay never mixes two runs, then sends the bed's
/// transaction Bundle. Waits for the server to answer /metadata first (HAPI needs about a minute to start).
/// </summary>
public static class WardFhirLoader
{
    /// <summary>
    /// A client that asks the server not to reuse cached search results. HAPI caches identical searches for 60 s
    /// by default, which made a second load within a minute "see" no old Observations and leave them behind.
    /// </summary>
    public static FhirClient CreateClient(string baseUrl)
    {
        var client = new FhirClient(baseUrl);
        client.RequestHeaders?.Add("Cache-Control", "no-cache");
        return client;
    }

    public static async Task LoadAsync(FhirClient client, IReadOnlyList<WardBed> ward, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(ward);

        await WaitUntilReadyAsync(client, log, cancellationToken);
        foreach (var bed in ward)
        {
            var removed = await DeleteObservationsAsync(client, bed.PatientId, cancellationToken);
            var bundle = WardFhirBundle.Build(bed);
            await client.TransactionAsync(bundle, cancellationToken);
            log?.Invoke($"{bed.Bed} {bed.PatientName}: removed {removed} old observations, wrote {bundle.Entry.Count} resources");
        }
    }

    public static async Task WaitUntilReadyAsync(FhirClient client, Action<string>? log, CancellationToken cancellationToken, int attempts = 36)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                await client.CapabilityStatementAsync(ct: cancellationToken);
                return;
            }
            catch (Exception ex) when (i < attempts && ex is not OperationCanceledException)
            {
                log?.Invoke($"FHIR server not ready yet ({i}/{attempts}), retrying in 5 s...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private static async Task<int> DeleteObservationsAsync(FhirClient client, string patientId, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var query = new SearchParams().Where($"subject=Patient/{patientId}").LimitTo(200);
        query.Elements.Add("id");
        var page = await client.SearchAsync<Observation>(query, cancellationToken);
        while (page is not null)
        {
            ids.AddRange(page.Entry.Select(e => e.Resource).OfType<Observation>().Select(o => o.Id!));
            page = page.NextLink is null ? null : await client.ContinueAsync(page, ct: cancellationToken);
        }

        foreach (var id in ids)
        {
            await client.DeleteAsync($"Observation/{id}", ct: cancellationToken);
        }

        return ids.Count;
    }
}
