using Baton.Core.News2;
using Baton.Scenarios;
using Hl7.Fhir.Model;
using System.Globalization;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Baton.Api;

/// <summary>A patient on the ward as the API sees it.</summary>
public sealed record PatientRef(
    string Id,
    string Name,
    string? Bed,
    SpO2Scale SpO2Scale,
    Scale2Authorisation? Scale2Authorisation,
    DateOnly? BirthDate = null,
    string? Diagnosis = null)
{
    /// <summary>
    /// Reads Baton's bed identifier, Scale 2 decision extension and birth date from a FHIR Patient, and the
    /// diagnosis text from its Condition when one is given.
    /// </summary>
    public static PatientRef FromFhir(Patient patient, Condition? diagnosis = null)
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

        DateOnly? birthDate = DateOnly.TryParseExact(patient.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var born)
            ? born
            : null;

        return new PatientRef(
            patient.Id!,
            name,
            bed,
            decision is null ? SpO2Scale.Scale1 : SpO2Scale.Scale2,
            decision,
            birthDate,
            diagnosis?.Code?.Text);
    }
}

/// <summary>Something that happened around a patient: concern flag, alert, doctor called or arrived, and so on.</summary>
public sealed record WardEvent(DateTimeOffset At, ScenarioEventKind Kind, string Text);

/// <summary>
/// A patient's events. <see cref="Available"/> is false when the source cannot provide events at all (the FHIR
/// server, until escalation resources are stored in FHIR), which is not the same as "nothing happened".
/// </summary>
public sealed record PatientEvents(bool Available, IReadOnlyList<WardEvent> Events)
{
    public static PatientEvents Unavailable { get; } = new(false, []);
}

/// <summary>Where the API gets patients, vital-sign Observations and events from.</summary>
public interface IVitalSignsSource
{
    Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken);

    /// <summary>All vital-sign Observations of the patient taken at or before <paramref name="until"/>.</summary>
    Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken);

    /// <summary>The patient's events at or before <paramref name="until"/>, oldest first.</summary>
    Task<PatientEvents> GetEventsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken);
}

/// <summary>The synthetic ward generated in memory (no FHIR server needed). Built the same way it is loaded into HAPI.</summary>
public sealed class InMemoryVitalSignsSource : IVitalSignsSource
{
    private readonly IReadOnlyList<PatientRef> patients;
    private readonly Dictionary<string, List<Observation>> observations;
    private readonly Dictionary<string, List<WardEvent>> events;

    public InMemoryVitalSignsSource(DateTimeOffset shiftStart, int seed, double treatmentDelayHours = 0, int beds = Ward.DefaultBeds)
    {
        var ward = Ward.Generate(shiftStart, seed, treatmentDelayHours: treatmentDelayHours, beds: beds);
        var bundles = ward.Select(WardFhirBundle.Build).ToList();
        patients = bundles.Select(b => PatientRef.FromFhir(
            b.Entry.Select(e => e.Resource).OfType<Patient>().Single(),
            b.Entry.Select(e => e.Resource).OfType<Condition>().SingleOrDefault())).ToList();
        events = ward.ToDictionary(
            b => b.PatientId,
            b => b.Patient.Events
                .Select(e => new WardEvent(ShiftSimulator.AtMinute(shiftStart, e.Hour), e.Kind, e.Text))
                .OrderBy(e => e.At)
                .ToList());
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

    public Task<PatientEvents> GetEventsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        IReadOnlyList<WardEvent> result = events.TryGetValue(patientId, out var list) ? list.Where(e => e.At <= until).ToList() : [];
        return Task.FromResult(new PatientEvents(true, result));
    }
}

/// <summary>Reads the ward from a FHIR R4 server such as HAPI (Firely FhirClient), following result pages.</summary>
public sealed class HapiVitalSignsSource(FhirClient client) : IVitalSignsSource
{
    public static FhirClient CreateClient(string baseUrl) => WardFhirLoader.CreateClient(baseUrl);

    public async Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken)
    {
        var query = new SearchParams().Where($"identifier={WardFhirBundle.BedSystem}|").LimitTo(100);
        query.RevInclude.Add(("Condition:subject", IncludeModifier.None));
        var resources = await All<Resource>(await client.SearchAsync<Patient>(query, cancellationToken), cancellationToken);
        var conditions = resources.OfType<Condition>()
            .Where(c => c.Subject?.Reference is not null)
            .GroupBy(c => c.Subject!.Reference!)
            .ToDictionary(g => g.Key, g => g.First());
        return resources.OfType<Patient>()
            .Select(p => PatientRef.FromFhir(p, conditions.GetValueOrDefault($"Patient/{p.Id}")))
            .OrderBy(p => p.Bed, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Events are not stored in FHIR yet (escalation resources come later), so they are unavailable.</summary>
    public Task<PatientEvents> GetEventsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken) =>
        Task.FromResult(PatientEvents.Unavailable);

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
