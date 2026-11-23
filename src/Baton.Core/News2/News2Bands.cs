namespace Baton.Core.News2;

/// <summary>
/// Points for each NEWS2 parameter, transcribed from the RCP NEWS2 2017 chart (Chart 1).
/// Inputs are assumed valid; validation and rounding happen in <see cref="News2Calculator"/>.
/// </summary>
internal static class News2Bands
{
    // Respiration rate (per minute): ≤8 → 3, 9–11 → 1, 12–20 → 0, 21–24 → 2, ≥25 → 3.
    public static int Respiration(int rate) => rate switch
    {
        <= 8 => 3,
        <= 11 => 1,
        <= 20 => 0,
        <= 24 => 2,
        _ => 3,
    };

    // SpO2 Scale 1 (%): ≤91 → 3, 92–93 → 2, 94–95 → 1, ≥96 → 0.
    public static int SpO2Scale1(int spo2) => spo2 switch
    {
        <= 91 => 3,
        <= 93 => 2,
        <= 95 => 1,
        _ => 0,
    };

    // SpO2 Scale 2 (%): ≤83 → 3, 84–85 → 2, 86–87 → 1, 88–92 → 0, ≥93 on air → 0,
    // 93–94 on oxygen → 1, 95–96 on oxygen → 2, ≥97 on oxygen → 3.
    public static int SpO2Scale2(int spo2, bool onSupplementalOxygen) => spo2 switch
    {
        <= 83 => 3,
        <= 85 => 2,
        <= 87 => 1,
        <= 92 => 0,
        _ when !onSupplementalOxygen => 0,
        <= 94 => 1,
        <= 96 => 2,
        _ => 3,
    };

    // Air or oxygen: air → 0, oxygen → 2 (on both SpO2 scales).
    public static int SupplementalOxygen(bool onSupplementalOxygen) => onSupplementalOxygen ? 2 : 0;

    // Systolic blood pressure (mmHg): ≤90 → 3, 91–100 → 2, 101–110 → 1, 111–219 → 0, ≥220 → 3.
    public static int Systolic(int mmHg) => mmHg switch
    {
        <= 90 => 3,
        <= 100 => 2,
        <= 110 => 1,
        <= 219 => 0,
        _ => 3,
    };

    // Pulse (per minute): ≤40 → 3, 41–50 → 1, 51–90 → 0, 91–110 → 1, 111–130 → 2, ≥131 → 3.
    public static int Pulse(int rate) => rate switch
    {
        <= 40 => 3,
        <= 50 => 1,
        <= 90 => 0,
        <= 110 => 1,
        <= 130 => 2,
        _ => 3,
    };

    // Temperature (°C, already rounded to one decimal): ≤35.0 → 3, 35.1–36.0 → 1, 36.1–38.0 → 0,
    // 38.1–39.0 → 1, ≥39.1 → 2.
    public static int Temperature(decimal roundedCelsius) => roundedCelsius switch
    {
        <= 35.0m => 3,
        <= 36.0m => 1,
        <= 38.0m => 0,
        <= 39.0m => 1,
        _ => 2,
    };

    // Consciousness: Alert → 0; new confusion, V, P or U → 3. RCP: treat confusion as new unless
    // known to be chronic; chronic confusion is not scored.
    public static int Consciousness(Consciousness level, ConfusionOnset onset) => level switch
    {
        News2.Consciousness.Alert => 0,
        News2.Consciousness.Confusion => onset == ConfusionOnset.Chronic ? 0 : 3,
        _ => 3,
    };
}
