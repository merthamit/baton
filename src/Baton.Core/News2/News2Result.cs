namespace Baton.Core.News2;

/// <summary>
/// The outcome of a NEWS2 calculation. When any parameter cannot be scored the result is incomplete:
/// there is no total, risk or response, but the parameters that could be scored are still reported.
/// </summary>
public sealed record News2Result
{
    /// <summary>True when every parameter was scored.</summary>
    public required bool IsComplete { get; init; }

    /// <summary>Aggregate score 0–20, only when complete.</summary>
    public int? Total { get; init; }

    /// <summary>
    /// Only when incomplete: the sum of the parameters that could be scored. Missing parameters can
    /// only add points, so the real total is at least this. Not a NEWS2 score.
    /// </summary>
    public int? MinimumTotal { get; init; }

    /// <summary>Points per parameter; null when a parameter could not be scored. All seven keys are present.</summary>
    public required IReadOnlyDictionary<News2Parameter, int?> SubScores { get; init; }

    /// <summary>True when any scored parameter scored 3, even if the result is incomplete.</summary>
    public required bool HasSingleParameterScoreOf3 { get; init; }

    public required IReadOnlyList<News2Parameter> ParametersScoring3 { get; init; }

    public RiskLevel? Risk { get; init; }

    public ClinicalResponse? Response { get; init; }

    public string? MinimumMonitoringFrequency { get; init; }

    public required IReadOnlyList<News2Problem> Problems { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    public required string AlgorithmVersion { get; init; }
}
