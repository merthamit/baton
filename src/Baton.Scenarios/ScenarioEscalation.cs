using Baton.Core.Escalation;

namespace Baton.Scenarios;

/// <summary>
/// Turns a simulated bed's scenario events into escalation records, without changing the card's content: the doctor's
/// arrival text is the assessment, the treatment text is the plan. The cards record critical care being called but not
/// arriving, which would read as "the team never came"; that call gets a simulated arrival, labelled as such.
/// </summary>
public static class ScenarioEscalation
{
    public const string ScenarioNurse = "Practitioner/synthetic-nurse";
    public const string OnCallDoctor = "Practitioner/synthetic-oncall";
    public const string SeniorDoctor = "Practitioner/synthetic-senior";
    public const string CriticalCareTeam = "Practitioner/synthetic-cct";

    /// <summary>Baton's assumed arrival time for a critical care team call with no recorded arrival.</summary>
    public static readonly TimeSpan CriticalCareArrival = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<EscalationRecord> From(WardBed bed, DateTimeOffset shiftStart)
    {
        ArgumentNullException.ThrowIfNull(bed);

        var records = new List<EscalationRecord>();
        string? onCall = null;
        var arrived = false;
        var number = 0;

        foreach (var e in bed.Patient.Events.OrderBy(e => e.Hour))
        {
            var at = ShiftSimulator.AtMinute(shiftStart, e.Hour);
            switch (e.Kind)
            {
                case ScenarioEventKind.Concern:
                    records.Add(new ConcernRaised(at, ScenarioNurse, e.Text));
                    break;

                case ScenarioEventKind.DoctorCalled:
                    onCall = $"call-{bed.PatientId}-{++number}";
                    arrived = false;
                    records.Add(new Called(at, ScenarioNurse, onCall, EscalationTier.OnCallDoctor));
                    break;

                case ScenarioEventKind.DoctorArrived when onCall is not null && !arrived:
                    arrived = true;
                    records.Add(new Arrived(at, OnCallDoctor, onCall, Simulated: false));
                    records.Add(new Assessed(at, OnCallDoctor, onCall, e.Text));
                    break;

                case ScenarioEventKind.DoctorArrived:
                    // The doctor was already on the ward (no open call): the call and the arrival share the minute.
                    onCall = $"call-{bed.PatientId}-{++number}";
                    arrived = true;
                    records.Add(new Called(at, ScenarioNurse, onCall, EscalationTier.OnCallDoctor));
                    records.Add(new Arrived(at, OnCallDoctor, onCall, Simulated: false));
                    records.Add(new Assessed(at, OnCallDoctor, onCall, e.Text));
                    break;

                case ScenarioEventKind.TreatmentStarted when onCall is not null && arrived:
                    records.Add(new Planned(at, OnCallDoctor, onCall, e.Text));
                    break;

                case ScenarioEventKind.CriticalCareCalled:
                    var call = $"call-{bed.PatientId}-{++number}";
                    records.Add(new Called(at, ScenarioNurse, call, EscalationTier.CriticalCare));
                    records.Add(new Arrived(at + CriticalCareArrival, CriticalCareTeam, call, Simulated: true));
                    break;
            }
        }

        return records;
    }
}
