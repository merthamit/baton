using Baton.Core.News2;
using Baton.Scenarios;
using Hl7.Fhir.Model;
using System.Globalization;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Baton.Api;

/// <summary>A patient on the ward as the API sees it.</summary>
public sealed record PatientRef(string Id, string Name, string? Bed, SpO2Scale SpO2Scale, Scale2Authorisation? Scale2Authorisation)
{
    /// <summary>Reads Baton's bed identifier and Scale 2 decision extension from a FHIR Patient.</summary>
    public static PatientRef FromFhir(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        var bed = patient.Identifier.FirstOrDefault(i => i.System == WardFhirBundle.BedSystem)?.Value;
        var name = patient.Name.FirstOrDefault()?.Text ?? patient.Id ?? "Unknown";

        Scale2Authorisation? decision = null;
        var extension = patient.GetExtension(WardFhirBundle.Scale2Extension);
        if (extension is not null
            && extension.GetStringExtension("authorisedBy") is { } by
            && extension.GetExtension("authorisedAt")?.Value is FhirDateTime when
            && when.TryToDateTimeOffset(out var at))
        {
            decision = new Scale2Authorisation(by, at);
        }

        return new PatientRef(patient.Id!, name, bed, decision is null ? SpO2Scale.Scale1 : SpO2Scale.Scale2, decision);
    }
}

/// <summary>Where the API gets patients and vital-sign Observations from.</summary>
public interface IVitalSignsSource
{
    Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken);

    /// <summary>All vital-sign Observations of the patient taken at or before <paramref name="until"/>.</summary>
    Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken);
}

/// <summary>The synthetic ward generated in memory (no FHIR server needed). Built the same way it is loaded into HAPI.</summary>
public sealed class InMemoryVitalSignsSource : IVitalSignsSource
{
    private readonly IReadOnlyList<PatientRef> patients;
    private readonly Dictionary<string, List<Observation>> observations;

    public InMemoryVitalSignsSource(DateTimeOffset shiftStart, int seed)
    {
        var bundles = Ward.Generate(shiftStart, seed).Select(WardFhirBundle.Build).ToList();
        patients = bundles.Select(b => PatientRef.FromFhir(b.Entry.Select(e => e.Resource).OfType<Patient>().Single())).ToList();
        observations = bundles.ToDictionary(
            b => b.Entry.Select(e => e.Resource).OfType<Patient>().Single().Id!,
            b => b.Entry.Select(e => e.Resource).OfType<Observation>().ToList());
    }

    public Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken) => Task.FromResult(patients);

    public Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        IReadOnlyList<Observation> result = observations.TryGetValue(patientId, out var list)
            ? list.Where(o => WardService.TimeOf(o) is { } t && t <= until).ToList()
            : [];
        return Task.FromResult(result);
    }
}

/// <summary>Reads the ward from a FHIR R4 server such as HAPI (Firely FhirClient), following result pages.</summary>
public sealed class HapiVitalSignsSource(FhirClient client) : IVitalSignsSource
{
    public static FhirClient CreateClient(string baseUrl) => WardFhirLoader.CreateClient(baseUrl);

    public async Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken)
    {
        var query = new SearchParams().Where($"identifier={WardFhirBundle.BedSystem}|").LimitTo(100);
        var patients = await All<Patient>(await client.SearchAsync<Patient>(query, cancellationToken), cancellationToken);
        return patients.Select(PatientRef.FromFhir).OrderBy(p => p.Bed, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var query = new SearchParams()
            .Where($"subject=Patient/{patientId}")
            .Where("date=le" + until.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
            .LimitTo(500);
        return await All<Observation>(await client.SearchAsync<Observation>(query, cancellationToken), cancellationToken);
    }

    private async Task<List<T>> All<T>(Bundle? page, CancellationToken cancellationToken)
        where T : Resource
    {
        var result = new List<T>();
        while (page is not null)
        {
            result.AddRange(page.Entry.Select(e => e.Resource).OfType<T>());
            page = page.NextLink is null ? null : await client.ContinueAsync(page, ct: cancellationToken);
        }

        return result;
    }
}
