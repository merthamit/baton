using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

internal static class TestInputs
{
    /// <summary>A complete set of observations where every parameter scores 0.</summary>
    public static News2Input Normal() => new()
    {
        RespirationRate = 16,
        SpO2 = 97,
        SpO2Scale = SpO2Scale.Scale1,
        OnSupplementalOxygen = false,
        SystolicBp = 120,
        PulseRate = 70,
        TemperatureCelsius = 37.0m,
        Consciousness = Consciousness.Alert,
    };

    public static Scale2Authorisation ValidScale2Decision() =>
        new("Dr Example (respiratory registrar)", new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
}
