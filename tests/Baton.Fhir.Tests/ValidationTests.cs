using Baton.Core.News2;
using Firely.Fhir.Validation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;

namespace Baton.Fhir.Tests;

// The research report's four scenarios, read from FHIR, scored, written back as NEWS2 Observations and
// validated against the base FHIR R4 Observation definition. Each result is also saved under
// artifacts/fhir-examples so CI can run the official HL7 validator on it.
public class ValidationTests
{
    private static readonly Lazy<Validator> SharedValidator = new(() =>
    {
        var resolver = new CachedResolver(ZipSource.CreateValidationSource());
        return new Validator(resolver, new LocalTerminologyService(resolver));
    });

    public static TheoryData<string, int> Scenarios => new()
    {
        { "stable", 0 },
        { "quiet-sepsis", 7 },
        { "copd-scale2", 5 },
        { "hidden-bleed", 9 },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Scenario_ProducesAValidNews2Observation(string name, int expectedTotal)
    {
        var (observations, options) = Build(name);
        var reading = VitalSignsReader.Read(observations, options);
        var result = News2Calculator.Calculate(reading.Input);

        var news2 = News2ObservationWriter.Create(result, reading, new ResourceReference("Patient/synthetic-1"), Fixtures.Now);
        news2.Id = $"news2-{name}";

        Assert.Equal(expectedTotal, result.Total);
        var outcome = SharedValidator.Value.Validate(news2);
        var errors = outcome.Issue
            .Where(i => i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal)
            .Select(i => $"{i.Severity}: {i.Details?.Text ?? i.Diagnostics} @ {string.Join(",", i.Expression)}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        SaveExample(news2);
    }

    [Fact]
    public void Validator_RejectsAnObservationWithoutStatusAndCode()
    {
        // Proves the validator is really checking: status and code are required (1..1) in FHIR R4.
        var broken = new Observation { Value = new Quantity { Value = 3, System = "http://unitsofmeasure.org", Code = "{score}" } };

        var outcome = SharedValidator.Value.Validate(broken);

        Assert.Contains(outcome.Issue, i => i.Severity is OperationOutcome.IssueSeverity.Error);
    }

    private static (List<Observation> Observations, ReadOptions Options) Build(string name)
    {
        var t = Fixtures.Now.AddMinutes(-10);
        var options = Fixtures.Options();
        (decimal rr, decimal spo2, decimal o2Flow, decimal sbp, decimal hr, decimal temp, string acvpu) v = name switch
        {
            "stable" => (16, 97, 0, 124, 78, 36.8m, "LA9340-6"),
            "quiet-sepsis" => (23, 94, 0, 102, 112, 38.6m, "LA9340-6"),
            "copd-scale2" => (22, 90, 2, 132, 96, 37.2m, "LA9340-6"),
            "hidden-bleed" => (21, 96, 0, 96, 124, 36.2m, "LA6560-2"),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        if (name == "copd-scale2")
        {
            options = options with
            {
                SpO2Scale = SpO2Scale.Scale2,
                Scale2Authorisation = new Scale2Authorisation("Dr Example (respiratory registrar)", Fixtures.Now.AddDays(-1)),
            };
        }

        List<Observation> observations =
        [
            Fixtures.Vital($"{name}-rr", "9279-1", v.rr, "/min", t),
            Fixtures.Vital($"{name}-spo2", "59408-5", v.spo2, "%", t),
            Fixtures.Vital($"{name}-o2", "3151-8", v.o2Flow, "L/min", t),
            Fixtures.Vital($"{name}-sbp", "8480-6", v.sbp, "mm[Hg]", t),
            Fixtures.Vital($"{name}-hr", "8867-4", v.hr, "/min", t),
            Fixtures.Consciousness($"{name}-acvpu", v.acvpu, t),
            Fixtures.Vital($"{name}-temp", "8310-5", v.temp, "Cel", t),
        ];
        return (observations, options);
    }

    private static void SaveExample(Observation observation)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Baton.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts", "fhir-examples"));
        File.WriteAllText(Path.Combine(folder.FullName, $"{observation.Id}.json"), observation.ToJson(pretty: true));
    }
}
