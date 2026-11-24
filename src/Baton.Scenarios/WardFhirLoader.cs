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
            var chain = await DeleteEscalationAsync(client, stale, cancellationToken);
            var observations = await DeleteAllAsync<Observation>(client, $"subject=Patient/{stale}", cancellationToken);
            var conditions = await DeleteAllAsync<Condition>(client, $"subject=Patient/{stale}", cancellationToken);
            await client.DeleteAsync($"Patient/{stale}", ct: cancellationToken);
            log?.Invoke($"Removed stale patient {stale} ({observations} observations, {conditions} conditions, {chain} escalation resources)");
        }

        foreach (var bed in ward)
        {
            await DeleteEscalationAsync(client, bed.PatientId, cancellationToken);
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

    /// <summary>
    /// Deletion order for a patient's data. A server that checks references refuses to delete a resource something still
    /// points at: Provenance points at the chain resources, and a call Task points at its NEWS2 Observation.
    /// </summary>
    public static IReadOnlyList<string> DeleteOrder { get; } = ["Provenance", "Task", "Flag", "Communication", "Observation"];

    private static async Task<int> DeleteEscalationAsync(FhirClient client, string patientId, CancellationToken cancellationToken)
    {
        var targets = new List<string>();
        targets.AddRange((await IdsAsync<Hl7.Fhir.Model.Task>(client, $"patient=Patient/{patientId}", cancellationToken)).Select(id => $"Task/{id}"));
        targets.AddRange((await IdsAsync<Flag>(client, $"patient=Patient/{patientId}", cancellationToken)).Select(id => $"Flag/{id}"));
        targets.AddRange((await IdsAsync<Communication>(client, $"subject=Patient/{patientId}", cancellationToken)).Select(id => $"Communication/{id}"));

        foreach (var target in targets)
        {
            await DeleteAllAsync<Provenance>(client, $"target={target}", cancellationToken);
        }

        var tasks = TaskDeletionOrder(targets.Where(t => t.StartsWith("Task/", StringComparison.Ordinal)));
        foreach (var target in tasks.Concat(targets.Where(t => !t.StartsWith("Task/", StringComparison.Ordinal))))
        {
            await client.DeleteAsync(target, ct: cancellationToken);
        }

        return targets.Count;
    }

    /// <summary>
    /// Call Tasks, latest call first: a later call's Task is <c>partOf</c> the earlier one, so the earlier one can only be
    /// deleted after it. Call ids end in their sequence number (<c>call-{patient}-{n}</c>).
    /// </summary>
    public static IReadOnlyList<string> TaskDeletionOrder(IEnumerable<string> taskReferences)
    {
        ArgumentNullException.ThrowIfNull(taskReferences);
        static int Sequence(string reference) =>
            int.TryParse(reference[(reference.LastIndexOf('-') + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        return taskReferences.OrderByDescending(Sequence).ToList();
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

    /// <summary>NEWS2 Observations first: they point at the readings they were scored from (<c>derivedFrom</c>).</summary>
    public static IReadOnlyList<string> ObservationDeletionOrder(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.OrderByDescending(id => id.StartsWith("news2-", StringComparison.Ordinal)).ToList();
    }

    private static async Task<int> DeleteAllAsync<T>(FhirClient client, string criterion, CancellationToken cancellationToken)
        where T : Resource
    {
        var found = await IdsAsync<T>(client, criterion, cancellationToken);
        var ids = typeof(T) == typeof(Observation) ? ObservationDeletionOrder(found) : found;
        var type = ModelInfo.GetFhirTypeNameForType(typeof(T));
        foreach (var id in ids)
        {
            await client.DeleteAsync($"{type}/{id}", ct: cancellationToken);
        }

        return ids.Count;
    }
}
