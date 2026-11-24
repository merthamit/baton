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
public sealed record BedPlan(
    string Bed,
    string PatientName,
    int Age,
    ScenarioCard Card,
    double ShiftHours,
    string? Diagnosis = null,
    LateObservation? Late = null);

public sealed record WardBed(string Bed, string PatientId, string PatientName, int Age, string Diagnosis, SimulatedPatient Patient);

/// <summary>
/// The synthetic ward: eight story beds (the five cards, some started at different times) and, for density
/// checks, up to 22 more beds that reuse the cards with other start times. Beds are numbered "01"–"30".
/// </summary>
public static class Ward
{
    public const int DefaultBeds = 8;
    public const int MaxBeds = 30;

    public static IReadOnlyList<BedPlan> DefaultPlan { get; } =
    [
        new("01", "Hasan T.", 58, ScenarioCards.Stable, 0),
        new("02", "Ayşe K.", 71, ScenarioCards.Sepsis, 0),
        new("03", "Elif S.", 44, ScenarioCards.Stable, 2, "Lower-leg cellulitis"),
        // Decision 9A: while the team is at bed 02's emergency (critical care called 00:20), bed 04's 00:30 set waits.
        new("04", "Mehmet D.", 66, ScenarioCards.HiddenBleed, 0, Late: new LateObservation(16.5, 45, "the team was at bed 02's emergency")),
        new("05", "Nuri A.", 73, ScenarioCards.CopdScale2, 0),
        new("06", "Zeynep B.", 52, ScenarioCards.Recovering, 0),
        new("07", "İbrahim Y.", 80, ScenarioCards.Sepsis, -6, "Urinary tract infection, catheterised"),
        new("08", "Fatma Ö.", 61, ScenarioCards.Stable, 1, "Diabetic foot ulcer"),
    ];

    // Invented names for beds 09–30; none repeats a story bed.
    private static readonly string[] ExtraNames =
    [
        "Ali R.", "Emine C.", "Mustafa G.", "Hatice P.", "Ahmet U.", "Leyla E.", "Osman F.", "Sevgi H.",
        "Kemal İ.", "Gül N.", "Yusuf Ş.", "Meryem T.", "Halil V.", "Derya Z.", "Cem O.", "Aylin M.",
        "Murat L.", "Nermin J.", "Serkan Ç.", "Filiz Ü.", "Burak K.", "Selin A.",
    ];

    private static readonly ScenarioCard[] Cards =
        [ScenarioCards.Stable, ScenarioCards.Sepsis, ScenarioCards.HiddenBleed, ScenarioCards.CopdScale2, ScenarioCards.Recovering];

    /// <summary>The first <paramref name="beds"/> beds (1–30). The first eight are always <see cref="DefaultPlan"/>.</summary>
    public static IReadOnlyList<BedPlan> Plan(int beds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(beds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(beds, MaxBeds);

        return Enumerable.Range(0, beds).Select(i => i < DefaultPlan.Count
            ? DefaultPlan[i]
            : new BedPlan(
                (i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                ExtraNames[i - DefaultPlan.Count],
                30 + (i * 17 % 59),
                Cards[i % Cards.Length],
                (i * 5 % 13) - 6)).ToList();
    }

    /// <summary>
    /// Each bed's noise seed. Plain arithmetic on purpose: <see cref="HashCode"/> is randomised per process,
    /// which would give a different ward on every run.
    /// </summary>
    public static int SeedFor(int seed, int bedIndex) => unchecked((seed * 1_000_000) + bedIndex);

    public static IReadOnlyList<WardBed> Generate(DateTimeOffset shiftStart, int seed, double noiseLevel = 1, double treatmentDelayHours = 0, int beds = DefaultBeds) =>
        Plan(beds).Select((plan, i) =>
        {
            var card = CardShift.Shift(plan.Card, plan.ShiftHours);
            var options = new SimulationOptions(shiftStart)
            {
                Seed = SeedFor(seed, i),
                NoiseLevel = noiseLevel,
                TreatmentDelayHours = treatmentDelayHours,
                LateObservation = plan.Late,
            };
            return new WardBed(
                plan.Bed,
                $"synthetic-{plan.Bed}",
                plan.PatientName,
                plan.Age,
                plan.Diagnosis ?? card.Diagnosis,
                ShiftSimulator.Run(card, options));
        }).ToList();
}
