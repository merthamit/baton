namespace Baton.Core.News2;

/// <summary>
/// One set of observations for NEWS2. Every measurement is nullable so that "not measured" is never
/// confused with a value of zero. NEWS2 applies to adults aged 16 and over who are not pregnant.
/// </summary>
public sealed record News2Input
{
    /// <summary>Breaths per minute.</summary>
    public int? RespirationRate { get; init; }

    /// <summary>Peripheral oxygen saturation, percent.</summary>
    public int? SpO2 { get; init; }

    public SpO2Scale SpO2Scale { get; init; } = SpO2Scale.Scale1;

    /// <summary>Required when <see cref="SpO2Scale"/> is Scale 2.</summary>
    public Scale2Authorisation? Scale2Authorisation { get; init; }

    /// <summary>True on supplemental oxygen, false on room air, null when not recorded. Room air is never assumed.</summary>
    public bool? OnSupplementalOxygen { get; init; }

    /// <summary>Systolic blood pressure, mmHg.</summary>
    public int? SystolicBp { get; init; }

    /// <summary>Beats per minute.</summary>
    public int? PulseRate { get; init; }

    /// <summary>Degrees Celsius.</summary>
    public decimal? TemperatureCelsius { get; init; }

    public Consciousness? Consciousness { get; init; }

    /// <summary>Only used when <see cref="Consciousness"/> is Confusion.</summary>
    public ConfusionOnset ConfusionOnset { get; init; } = ConfusionOnset.Unknown;
}
