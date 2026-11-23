namespace Baton.Fhir;

/// <summary>LOINC codes and answers used to read NEWS2 vital signs from FHIR R4.</summary>
internal static class Loinc
{
    public const string System = "http://loinc.org";
    public const string Ucum = "http://unitsofmeasure.org";

    public static readonly string[] RespirationRate = ["9279-1"];
    public static readonly string[] SpO2 = ["2708-6", "59408-5"];
    public const string BloodPressurePanel = "85354-9";
    public const string Systolic = "8480-6";
    public static readonly string[] PulseRate = ["8867-4"];
    public static readonly string[] Temperature = ["8310-5"];
    public const string OxygenFlow = "3151-8";
    public const string InspiredOxygen = "3150-0";
    public static readonly string[] Consciousness = ["67775-7", "80288-4"];

    public static readonly Dictionary<string, Core.News2.Consciousness> ConsciousnessAnswers = new()
    {
        ["LA9340-6"] = Core.News2.Consciousness.Alert,
        ["LA6560-2"] = Core.News2.Consciousness.Confusion,
        ["LA17108-4"] = Core.News2.Consciousness.Voice,
        ["LA17107-6"] = Core.News2.Consciousness.Pain,
        ["LA9343-0"] = Core.News2.Consciousness.Unresponsive,
    };
}
