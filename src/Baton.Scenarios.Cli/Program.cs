using System.Globalization;
using Baton.Scenarios;
using Hl7.Fhir.Rest;

// Synthetic ward for Baton (8 story beds by default, up to 30).
//   Write JSON:       dotnet run --project src/Baton.Scenarios.Cli -- [--seed 42] [--delay 0] [--noise 1] [--beds 8] [--out artifacts/ward.json]
//   Load into FHIR:   dotnet run --project src/Baton.Scenarios.Cli -- load --fhir http://localhost:8080/fhir [--seed 42] [--delay 0] [--beds 8]
//   New observation:  dotnet run --project src/Baton.Scenarios.Cli -- observe --bed 02 --at 2026-11-23T15:10:00+03:00
//                       --rr 26 --spo2 91 --o2 2 --sbp 95 --hr 118 --temp 38.9 --acvpu A --fhir http://localhost:8080/fhir
if (args.Length > 0 && args[0] == "observe")
{
    var (request, problem) = ObserveCommand.Parse(args[1..]);
    if (request is null)
    {
        Console.Error.WriteLine(problem);
        return 1;
    }

    try
    {
        await WardFhirLoader.CreateClient(request.FhirBaseUrl).TransactionAsync(ObserveCommand.ToBundle(request));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Writing to {request.FhirBaseUrl} failed: {ex.Message}");
        return 1;
    }

    Console.WriteLine($"Wrote 7 observations for {request.PatientId} at {request.At:O}");
    return 0;
}

var load = args.Length > 0 && args[0] == "load";
var options = load ? args[1..] : args;

var seed = 42;
var delay = 0.0;
var noise = 1.0;
var beds = Ward.DefaultBeds;
var output = Path.Combine("artifacts", "ward.json");
string? fhir = null;

if (options.Length % 2 != 0)
{
    Console.Error.WriteLine($"Option {options[^1]} needs a value.");
    return 1;
}

for (var i = 0; i < options.Length; i += 2)
{
    var value = options[i + 1];
    switch (options[i])
    {
        case "--seed": seed = int.Parse(value, CultureInfo.InvariantCulture); break;
        case "--delay": delay = double.Parse(value, CultureInfo.InvariantCulture); break;
        case "--noise": noise = double.Parse(value, CultureInfo.InvariantCulture); break;
        case "--beds": beds = int.Parse(value, CultureInfo.InvariantCulture); break;
        case "--out": output = value; break;
        case "--fhir": fhir = value; break;
        default: Console.Error.WriteLine($"Unknown option {options[i]}"); return 1;
    }
}

if (delay < 0 || noise < 0)
{
    Console.Error.WriteLine("--delay and --noise must not be negative.");
    return 1;
}

if (beds is < 1 or > Ward.MaxBeds)
{
    Console.Error.WriteLine($"--beds must be between 1 and {Ward.MaxBeds}.");
    return 1;
}

// A fixed shift start keeps the output reproducible: 08:00 Turkey time.
var shiftStart = new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));
var ward = Ward.Generate(shiftStart, seed, noise, delay, beds);

if (!load)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, WardJson.Serialize(ward, shiftStart, seed, delay));
    Console.WriteLine($"Wrote {ward.Count} beds, {ward.Sum(b => b.Patient.Measurements.Count)} measurements to {output}");
    return 0;
}

if (fhir is null)
{
    Console.Error.WriteLine("load needs --fhir <base url>, e.g. http://localhost:8080/fhir");
    return 1;
}

try
{
    await WardFhirLoader.LoadAsync(WardFhirLoader.CreateClient(fhir), ward, Console.WriteLine);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Loading into {fhir} failed: {ex.Message}");
    return 1;
}

Console.WriteLine($"Loaded {ward.Count} beds into {fhir}");
return 0;
