using Baton.Core.News2;

namespace Baton.Fhir;

/// <summary>
/// SNOMED CT (UK edition) codes for the NEWS2 total and the seven sub-scores.
/// </summary>
internal static class Snomed
{
    public const string System = "http://snomed.info/sct";

    public const string News2Total = "1104051000000101";
    public const string News2TotalDisplay = "Royal College of Physicians National Early Warning Score 2 total score";

    public static (string Code, string Display) SubScore(News2Parameter parameter, SpO2Scale scale) => parameter switch
    {
        News2Parameter.RespirationRate => ("1104301000000104", Display("respiration rate")),
        News2Parameter.SpO2 when scale == SpO2Scale.Scale2 => ("1104321000000108", Display("SpO2 scale 2")),
        News2Parameter.SpO2 => ("1104311000000102", Display("SpO2 scale 1")),
        News2Parameter.SupplementalOxygen => ("1104331000000105", Display("air or oxygen")),
        News2Parameter.SystolicBp => ("1104341000000101", Display("systolic blood pressure")),
        News2Parameter.PulseRate => ("1104351000000103", Display("pulse")),
        News2Parameter.Consciousness => ("1104361000000100", Display("consciousness")),
        News2Parameter.Temperature => ("1104371000000107", Display("temperature")),
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "Unknown NEWS2 parameter."),
    };

    private static string Display(string name) => $"Royal College of Physicians National Early Warning Score 2 - {name} score";
}
