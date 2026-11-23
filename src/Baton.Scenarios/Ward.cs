namespace Baton.Scenarios;

/// <summary>Moves a whole card earlier or later in the shift. Events after the shift are dropped.</summary>
public static class CardShift
{
    public static ScenarioCard Shift(ScenarioCard card, double hours)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (hours == 0)
        {
            return card;
        }

        return card with
        {
            Keyframes = card.Keyframes.Select(k => k with { Hour = k.Hour + hours }).ToList(),
            Events = card.Events
                .Select(e => e with { Hour = e.Hour + hours })
                .Where(e => e.Hour < ShiftSimulator.ShiftHours)
                .ToList(),
            Delay = card.Delay is null ? null : card.Delay with { TreatmentHour = card.Delay.TreatmentHour + hours },
        };
    }
}

/// <summary>One bed of the synthetic ward. All names are invented.</summary>
public sealed record BedPlan(string Bed, string PatientName, int Age, ScenarioCard Card, double ShiftHours, string? Diagnosis = null);

public sealed record WardBed(string Bed, string PatientId, string PatientName, int Age, string Diagnosis, SimulatedPatient Patient);

/// <summary>The synthetic 8-bed ward: the five cards, some started at different times.</summary>
public static class Ward
{
    public static IReadOnlyList<BedPlan> DefaultPlan { get; } =
    [
        new("Y-01", "Hasan T.", 58, ScenarioCards.Stable, 0),
        new("Y-02", "Ayşe K.", 71, ScenarioCards.Sepsis, 0),
        new("Y-03", "Elif S.", 44, ScenarioCards.Stable, 2, "Lower-leg cellulitis"),
        new("Y-04", "Mehmet D.", 66, ScenarioCards.HiddenBleed, 0),
        new("Y-05", "Nuri A.", 73, ScenarioCards.CopdScale2, 0),
        new("Y-06", "Zeynep B.", 52, ScenarioCards.Recovering, 0),
        new("Y-07", "İbrahim Y.", 80, ScenarioCards.Sepsis, -6, "Urinary tract infection, catheterised"),
        new("Y-08", "Fatma Ö.", 61, ScenarioCards.Stable, 1, "Diabetic foot ulcer"),
    ];

    /// <summary>
    /// Each bed's noise seed. Plain arithmetic on purpose: <see cref="HashCode"/> is randomised per process,
    /// which would give a different ward on every run.
    /// </summary>
    public static int SeedFor(int seed, int bedIndex) => unchecked((seed * 1_000_000) + bedIndex);

    public static IReadOnlyList<WardBed> Generate(DateTimeOffset shiftStart, int seed, double noiseLevel = 1, double treatmentDelayHours = 0) =>
        DefaultPlan.Select((plan, i) =>
        {
            var card = CardShift.Shift(plan.Card, plan.ShiftHours);
            var options = new SimulationOptions(shiftStart)
            {
                Seed = SeedFor(seed, i),
                NoiseLevel = noiseLevel,
                TreatmentDelayHours = treatmentDelayHours,
            };
            return new WardBed(
                plan.Bed,
                $"synthetic-{plan.Bed.ToLowerInvariant()}",
                plan.PatientName,
                plan.Age,
                plan.Diagnosis ?? card.Diagnosis,
                ShiftSimulator.Run(card, options));
        }).ToList();
}
