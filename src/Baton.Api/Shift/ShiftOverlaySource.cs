using System.Globalization;
using Baton.Core.Escalation;
using Baton.Scenarios;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Baton.Api.Shift;

/// <summary>
/// The ward as one shift sees it (decision 21): the scenario before the shift's start, then only the visitor's bedside
/// sets and escalation records. Bedside sets become FHIR vital-sign Observations (issued = when they were entered).
/// </summary>
public sealed class ShiftOverlaySource(IVitalSignsSource inner, WardShift shift) : IVitalSignsSource
{
    private readonly DateTimeOffset start = shift.StartedAt;

    public Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken) => inner.GetPatientsAsync(cancellationToken);

    public async Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var past = await inner.GetObservationsAsync(patientId, until, cancellationToken);
        var list = past.Where(o => WardService.TimeOf(o) is { } t && t < start).ToList();
        foreach (var entry in shift.Observations.Where(o => o.PatientId == patientId && o.At <= until))
        {
            var stamp = string.Create(CultureInfo.InvariantCulture, $"live-{shift.Id[..8]}-{entry.Number}");
            foreach (var observation in ScenarioFhirExport.ToObservations(patientId, entry.At, entry.Vitals, stamp))
            {
                observation.Issued = entry.RecordedAt;
                list.Add(observation);
            }
        }

        return list;
    }

    public async Task<PatientEvents> GetEventsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var events = await inner.GetEventsAsync(patientId, until, cancellationToken);
        return events with { Events = events.Events.Where(e => e.At < start).ToList() };
    }

    public async Task<IReadOnlyList<EscalationRecord>> GetEscalationAsync(string patientId, DateTimeOffset until, string? language, CancellationToken cancellationToken)
    {
        var past = await inner.GetEscalationAsync(patientId, until, language, cancellationToken);
        return past.Where(r => r.At < start)
            .Concat(shift.RecordsOf(patientId).Where(r => r.At <= until))
            .OrderBy(r => r.At)
            .ToList();
    }

    /// <summary>The patient's scenario records before the shift's start (for answering a call left open there).</summary>
    public async Task<IReadOnlyList<EscalationRecord>> PastAsync(string patientId, CancellationToken cancellationToken) =>
        (await inner.GetEscalationAsync(patientId, start, null, cancellationToken)).Where(r => r.At < start).ToList();
}
