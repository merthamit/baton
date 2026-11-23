using Baton.Core.News2;

namespace Baton.Core.Tests.News2;

/// <summary>
/// An independent transcription of the RCP NEWS2 2017 chart for tests: each band is written as an
/// inclusive low–high range, unlike the engine's "less than or equal to" chain. Ranges stop at the
/// engine's plausibility limits. A value that falls in no band throws, so gaps in the table show up.
/// </summary>
internal static class RcpReferenceTable
{
    private static readonly Dictionary<News2Parameter, (decimal Low, decimal High, int Points)[]> Bands = new()
    {
        [News2Parameter.RespirationRate] = [(1, 8, 3), (9, 11, 1), (12, 20, 0), (21, 24, 2), (25, 80, 3)],
        [News2Parameter.SpO2] = [(1, 91, 3), (92, 93, 2), (94, 95, 1), (96, 100, 0)], // Scale 1
        [News2Parameter.SystolicBp] = [(1, 90, 3), (91, 100, 2), (101, 110, 1), (111, 219, 0), (220, 300, 3)],
        [News2Parameter.PulseRate] = [(1, 40, 3), (41, 50, 1), (51, 90, 0), (91, 110, 1), (111, 130, 2), (131, 300, 3)],
        [News2Parameter.Temperature] =
            [(25.0m, 35.0m, 3), (35.1m, 36.0m, 1), (36.1m, 38.0m, 0), (38.1m, 39.0m, 1), (39.1m, 45.0m, 2)],
    };

    public static int ExpectedPoints(News2Parameter parameter, decimal value)
    {
        foreach (var (low, high, points) in Bands[parameter])
        {
            if (value >= low && value <= high)
            {
                return points;
            }
        }

        throw new InvalidOperationException($"{parameter} {value} is in no RCP band of the reference table.");
    }
}
