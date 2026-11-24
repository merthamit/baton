using System.Globalization;
using Baton.Core.News2;

namespace Baton.Scenarios;

/// <summary>Plausible entry ranges for a bedside set (same as the <c>observe</c> command). Outside them: a typing error.</summary>
public static class VitalLimits
{
    public sealed record Problem(string Field, string Message);

    public static Problem? Check(VitalSet vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        static string N(decimal d) => d.ToString(CultureInfo.InvariantCulture);
        Problem? Range(string field, decimal value, decimal min, decimal max) =>
            value < min || value > max ? new(field, $"{field} must be from {N(min)} to {N(max)}.") : null;

        return Range("respirationRate", vitals.RespirationRate, 3, 80)
            ?? Range("spO2", vitals.SpO2, 50, 100)
            ?? Range("oxygenLitresPerMinute", vitals.OxygenLitresPerMinute, 0, 60)
            ?? Range("systolicBp", vitals.SystolicBp, 40, 300)
            ?? Range("pulseRate", vitals.PulseRate, 20, 250)
            ?? Range("temperatureCelsius", vitals.TemperatureCelsius, 30, 45)
            ?? (Enum.IsDefined(vitals.Consciousness) ? null : new("consciousness", "consciousness must be A, C, V, P or U."));
    }
}
