using Baton.Core.News2;

namespace Baton.Fhir;

/// <summary>How to read vital signs from FHIR.</summary>
/// <param name="Now">The moment the score is calculated for. Readings after it are rejected.</param>
/// <param name="MaxAge">Readings older than this are stale and are not used.</param>
public sealed record ReadOptions(DateTimeOffset Now, TimeSpan MaxAge)
{
    /// <summary>The reader never chooses Scale 2 itself; the caller passes the recorded decision.</summary>
    public SpO2Scale SpO2Scale { get; init; } = SpO2Scale.Scale1;

    public Scale2Authorisation? Scale2Authorisation { get; init; }
}
