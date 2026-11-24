using System.Globalization;
using System.Text.RegularExpressions;
using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Scenarios;

/// <summary>A new synthetic observation set to write for one bed (CLI <c>observe</c>, live board demo).</summary>
public sealed record ObserveRequest(string PatientId, DateTimeOffset At, VitalSet Vitals, string FhirBaseUrl);

/// <summary>
/// Parses and checks <c>observe</c>'s options, then builds the FHIR transaction: seven vital-sign Observations with the
/// scenario's LOINC writer, ids <c>{patientId}-live-{yyyyMMddHHmm}-{suffix}</c>, written with PUT.
/// </summary>
public static partial class ObserveCommand
{
    [GeneratedRegex(@"^\d{2}$")]
    private static partial Regex TwoDigits();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex Offset();

    private static readonly Dictionary<string, Consciousness> Acvpu = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = Consciousness.Alert,
        ["C"] = Consciousness.Confusion,
        ["V"] = Consciousness.Voice,
        ["P"] = Consciousness.Pain,
        ["U"] = Consciousness.Unresponsive,
    };

    public static (ObserveRequest? Request, string? Error) Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length % 2 != 0)
        {
            return (null, $"Option {args[^1]} needs a value.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            values[args[i]] = args[i + 1];
        }

        string[] required = ["--bed", "--at", "--rr", "--spo2", "--o2", "--sbp", "--hr", "--temp", "--acvpu", "--fhir"];
        if (required.FirstOrDefault(r => !values.ContainsKey(r)) is { } missing)
        {
            return (null, $"observe needs {missing}.");
        }

        if (values.Keys.FirstOrDefault(k => !required.Contains(k)) is { } unknown)
        {
            return (null, $"Unknown option {unknown}.");
        }

        if (!TwoDigits().IsMatch(values["--bed"]))
        {
            return (null, "--bed must be two digits, e.g. 02.");
        }

        if (!Offset().IsMatch(values["--at"]) || !DateTimeOffset.TryParse(values["--at"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return (null, "--at must be a date and time with an offset, e.g. 2026-11-23T15:10:00+03:00.");
        }

        string? error = null;
        int Int(string option, int min, int max)
        {
            if (int.TryParse(values[option], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max)
            {
                return n;
            }

            error ??= $"{option} must be a whole number from {min} to {max}.";
            return 0;
        }

        decimal Dec(string option, decimal min, decimal max)
        {
            if (decimal.TryParse(values[option], NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max)
            {
                return n;
            }

            error ??= $"{option} must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.";
            return 0;
        }

        var vitals = new VitalSet(
            Int("--rr", 3, 80),
            Int("--spo2", 50, 100),
            Dec("--o2", 0, 60),
            Int("--sbp", 40, 300),
            Int("--hr", 20, 250),
            Dec("--temp", 30, 45),
            Acvpu.TryGetValue(values["--acvpu"], out var level) ? level : Consciousness.Alert);
        if (!Acvpu.ContainsKey(values["--acvpu"]))
        {
            error ??= "--acvpu must be one of A, C, V, P, U.";
        }

        return error is null
            ? (new ObserveRequest($"synthetic-{values["--bed"]}", at, vitals, values["--fhir"]), null)
            : (null, error);
    }

    public static Bundle ToBundle(ObserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stamp = "live-" + request.At.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return new Bundle
        {
            Type = Bundle.BundleType.Transaction,
            Entry = ScenarioFhirExport.ToObservations(request.PatientId, request.At, request.Vitals, stamp)
                .Select(o => new Bundle.EntryComponent
                {
                    FullUrl = $"urn:uuid:{Guid.NewGuid()}",
                    Resource = o,
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = $"Observation/{o.Id}" },
                })
                .ToList(),
        };
    }
}
