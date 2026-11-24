using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Baton.Scenarios;

/// <summary>
/// Loads the synthetic ward into a FHIR server. It first removes Baton's synthetic patients that are not part of
/// this ward (an older bed numbering or a larger ward), with their Observations and Conditions. For each bed it then
/// deletes the patient's existing Observations, so loading again with another seed or delay never mixes two runs,
/// and sends the bed's transaction Bundle. Waits for the server to answer /metadata first (HAPI needs about a minute to start).
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
        foreach (var stale in StalePatientIds(await BatonPatientIdsAsync(client, cancellationToken), ward))
        {
            var observations = await DeleteAllAsync<Observation>(client, $"subject=Patient/{stale}", cancellationToken);
            var conditions = await DeleteAllAsync<Condition>(client, $"subject=Patient/{stale}", cancellationToken);
            await client.DeleteAsync($"Patient/{stale}", ct: cancellationToken);
            log?.Invoke($"Removed stale patient {stale} ({observations} observations, {conditions} conditions)");
        }

        foreach (var bed in ward)
        {
            var removed = await DeleteAllAsync<Observation>(client, $"subject=Patient/{bed.PatientId}", cancellationToken);
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

    /// <summary>Baton patients on the server that this ward does not contain.</summary>
    public static IReadOnlyList<string> StalePatientIds(IEnumerable<string> existing, IReadOnlyList<WardBed> ward)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(ward);
        var current = ward.Select(b => b.PatientId).ToHashSet(StringComparer.Ordinal);
        return existing.Where(id => !current.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
    }

    private static async Task<List<string>> BatonPatientIdsAsync(FhirClient client, CancellationToken cancellationToken) =>
        await IdsAsync<Patient>(client, $"identifier={WardFhirBundle.BedSystem}|", cancellationToken);

    private static async Task<List<string>> IdsAsync<T>(FhirClient client, string criterion, CancellationToken cancellationToken)
        where T : Resource
    {
        var ids = new List<string>();
        var query = new SearchParams().Where(criterion).LimitTo(200);
        query.Elements.Add("id");
        var page = await client.SearchAsync<T>(query, cancellationToken);
        while (page is not null)
        {
            ids.AddRange(page.Entry.Select(e => e.Resource).OfType<T>().Select(r => r.Id!));
            page = page.NextLink is null ? null : await client.ContinueAsync(page, ct: cancellationToken);
        }

        return ids;
    }

    private static async Task<int> DeleteAllAsync<T>(FhirClient client, string criterion, CancellationToken cancellationToken)
        where T : Resource
    {
        var ids = await IdsAsync<T>(client, criterion, cancellationToken);
        var type = ModelInfo.GetFhirTypeNameForType(typeof(T));
        foreach (var id in ids)
        {
            await client.DeleteAsync($"{type}/{id}", ct: cancellationToken);
        }

        return ids.Count;
    }
}
