using Baton.Core.Escalation;

namespace Baton.Scenarios;

public enum TryActionKind
{
    Concern,
    PatientOrFamilyConcern,
    Call,
    Assessed,
    Planned,
}

/// <summary>Something the user did in Try mode, at a moment of the shift.</summary>
public sealed record TryAction(TryActionKind Kind, DateTimeOffset At, string? Text = null, EscalationTier? Tier = null);

/// <summary>The Try patient's re-simulated shift and escalation records.</summary>
public sealed record TryResult(WardBed Bed, IReadOnlyList<EscalationRecord> Records);

/// <summary>
/// Try mode ("you are the nurse"): re-runs one bed from the user's actions. Before the start moment the scenario stands;
/// from it the scenario's own escalation (concerns, doctor calls, arrivals, treatment) is replaced by the user's. Each
/// call's arrival is simulated; treatment starts the card's arrival-to-treatment gap after the earliest arrival, and a
/// delay past the card's timely treatment worsens the patient by the card's delay rule (decision 2B). Calling earlier
/// than the card does not improve on the card's timely course: the model has no early-treatment benefit.
/// </summary>
public static class TryCourse
{
    public const string You = "Practitioner/synthetic-you";
    public const string Family = "RelatedPerson/synthetic-family";

    public static readonly TimeSpan DefaultOnCallArrival = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan SeniorArrival = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CriticalCareArrival = TimeSpan.FromMinutes(10);

    public static TryResult Run(DateTimeOffset shiftStart, int seed, int bedIndex, double noiseLevel, DateTimeOffset startAt, IReadOnlyList<TryAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var plan = Ward.Plan(Ward.MaxBeds)[bedIndex];
        var card = CardShift.Shift(plan.Card, plan.ShiftHours);
        var startHour = (startAt - shiftStart).TotalHours;

        // Watch, to keep what happened before the start and to read the card's own response times.
        var watch = Simulate(plan, card, shiftStart, seed, bedIndex, noiseLevel, delay: 0, until: null);
        var onCallGap = OnCallArrivalGap(watch.Patient.Events);
        var treatmentGap = ArrivalToTreatmentGap(card);

        var records = ScenarioEscalation.From(watch, shiftStart).Where(r => r.At < startAt).ToList();
        foreach (var open in records.OfType<Called>().Where(c => !records.OfType<Arrived>().Any(a => a.CallId == c.CallId)).ToList())
        {
            records.Add(Arrival(open.CallId, open.Tier, open.At, onCallGap));
        }

        var number = 0;
        string? latestArrivedCall = null;
        foreach (var action in actions.OrderBy(a => a.At))
        {
            switch (action.Kind)
            {
                case TryActionKind.Concern:
                    records.Add(new ConcernRaised(action.At, You, action.Text ?? ""));
                    break;
                case TryActionKind.PatientOrFamilyConcern:
                    records.Add(new PatientOrFamilyConcern(action.At, Family, action.Text ?? ""));
                    break;
                case TryActionKind.Call:
                    var callId = $"try-{bedIndex}-{++number}";
                    var tier = action.Tier ?? EscalationTier.OnCallDoctor;
                    records.Add(new Called(action.At, You, callId, tier));
                    records.Add(Arrival(callId, tier, action.At, onCallGap));
                    break;
                case TryActionKind.Assessed when (latestArrivedCall = LatestArrivedCall(records, action.At)) is not null:
                    records.Add(new Assessed(action.At, You, latestArrivedCall, action.Text ?? ""));
                    break;
                case TryActionKind.Planned when (latestArrivedCall = LatestArrivedCall(records, action.At)) is not null:
                    records.Add(new Planned(action.At, You, latestArrivedCall, action.Text ?? ""));
                    break;
            }
        }

        var delay = 0.0;
        if (card.Delay is { } rule)
        {
            var arrivals = records.OfType<Arrived>().Select(a => (a.At - shiftStart).TotalHours).ToList();
            var treatment = arrivals.Count > 0 ? arrivals.Min() + treatmentGap.TotalHours : ShiftSimulator.ShiftHours;
            delay = Math.Max(0, treatment - rule.TreatmentHour);
        }

        // The user's concerns replace the card's from the start on (they drive the 3C schedule).
        var events = card.Events
            .Where(e => !(e.Hour >= startHour && e.Kind is ScenarioEventKind.Concern or ScenarioEventKind.DoctorArrived or ScenarioEventKind.TreatmentStarted))
            .Concat(actions.Where(a => a.Kind == TryActionKind.Concern).Select(a => new ScenarioEvent((a.At - shiftStart).TotalHours, ScenarioEventKind.Concern, a.Text ?? "")))
            .OrderBy(e => e.Hour)
            .ToList();
        var bed = Simulate(plan, card with { Events = events }, shiftStart, seed, bedIndex, noiseLevel, delay, until: startHour);

        return new TryResult(bed, records.OrderBy(r => r.At).ToList());
    }

    /// <summary>The card's own call-to-arrival time, read from the Watch run (bed 02: 21 minutes).</summary>
    public static TimeSpan OnCallArrivalGap(IReadOnlyList<ScenarioEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var called = events.FirstOrDefault(e => e.Kind == ScenarioEventKind.DoctorCalled);
        var arrived = events.FirstOrDefault(e => e.Kind == ScenarioEventKind.DoctorArrived && called is not null && e.Hour >= called.Hour);
        return called is not null && arrived is not null ? TimeSpan.FromMinutes(Math.Round((arrived.Hour - called.Hour) * 60)) : DefaultOnCallArrival;
    }

    private static TimeSpan ArrivalToTreatmentGap(ScenarioCard card)
    {
        var arrived = card.Events.FirstOrDefault(e => e.Kind == ScenarioEventKind.DoctorArrived);
        var treated = card.Events.FirstOrDefault(e => e.Kind == ScenarioEventKind.TreatmentStarted);
        return arrived is not null && treated is not null && treated.Hour >= arrived.Hour
            ? TimeSpan.FromHours(treated.Hour - arrived.Hour)
            : TimeSpan.Zero;
    }

    private static Arrived Arrival(string callId, EscalationTier tier, DateTimeOffset calledAt, TimeSpan onCallGap) => tier switch
    {
        EscalationTier.SeniorDoctor => new Arrived(calledAt + SeniorArrival, ScenarioEscalation.SeniorDoctor, callId, Simulated: true),
        EscalationTier.CriticalCare => new Arrived(calledAt + CriticalCareArrival, ScenarioEscalation.CriticalCareTeam, callId, Simulated: true),
        _ => new Arrived(calledAt + onCallGap, ScenarioEscalation.OnCallDoctor, callId, Simulated: true),
    };

    private static string? LatestArrivedCall(List<EscalationRecord> records, DateTimeOffset at) =>
        records.OfType<Arrived>().Where(a => a.At <= at).OrderBy(a => a.At).LastOrDefault()?.CallId;

    private static WardBed Simulate(BedPlan plan, ScenarioCard card, DateTimeOffset shiftStart, int seed, int bedIndex, double noiseLevel, double delay, double? until)
    {
        var options = new SimulationOptions(shiftStart)
        {
            Seed = Ward.SeedFor(seed, bedIndex),
            NoiseLevel = noiseLevel,
            TreatmentDelayHours = delay,
            LateObservation = plan.Late,
            ScriptedEscalationUntilHour = until,
        };
        return new WardBed(plan.Bed, $"synthetic-{plan.Bed}", plan.PatientName, plan.Age, plan.Diagnosis ?? card.Diagnosis, ShiftSimulator.Run(card, options));
    }
}
