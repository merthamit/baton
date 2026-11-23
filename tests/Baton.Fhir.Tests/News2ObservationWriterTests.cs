using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir.Tests;

public class News2ObservationWriterTests
{
    private const string Snomed = "http://snomed.info/sct";
    private static readonly ResourceReference Patient = new("Patient/synthetic-1");
    private static readonly DateTimeOffset Effective = Fixtures.Now;

    private static (News2Result Result, VitalSignsReading Reading) Calculate(List<Observation> observations, ReadOptions? options = null)
    {
        var reading = VitalSignsReader.Read(observations, options ?? Fixtures.Options());
        return (News2Calculator.Calculate(reading.Input), reading);
    }

    private static Observation Write(List<Observation> observations, ReadOptions? options = null)
    {
        var (result, reading) = Calculate(observations, options);
        return News2ObservationWriter.Create(result, reading, Patient, Effective);
    }

    [Fact]
    public void Create_IncompleteResult_Throws()
    {
        var (result, reading) = Calculate([]);

        Assert.Throws<ArgumentException>(() => News2ObservationWriter.Create(result, reading, Patient, Effective));
    }

    [Fact]
    public void Create_UsesNews2TotalCodeStatusAndCategory()
    {
        var observation = Write(Fixtures.NormalSet());

        var coding = Assert.Single(observation.Code.Coding);
        Assert.Equal(Snomed, coding.System);
        Assert.Equal("1104051000000101", coding.Code);
        Assert.Equal(ObservationStatus.Final, observation.Status);
        var category = Assert.Single(Assert.Single(observation.Category).Coding);
        Assert.Equal("http://terminology.hl7.org/CodeSystem/observation-category", category.System);
        Assert.Equal("survey", category.Code);
        Assert.Equal("Patient/synthetic-1", observation.Subject!.Reference);
        Assert.Equal(new FhirDateTime(Effective).Value, Assert.IsType<FhirDateTime>(observation.Effective).Value);
        Assert.Equal("RCP-NEWS2-2017", observation.Method!.Text);
    }

    [Fact]
    public void Create_CodingsCarryTheirDisplays()
    {
        var observation = Write(Fixtures.NormalSet());

        Assert.Equal(
            "Royal College of Physicians National Early Warning Score 2 total score",
            observation.Code.Coding.Single().Display);
        Assert.Equal("Survey", observation.Category.Single().Coding.Single().Display);
        Assert.All(observation.Component, c =>
            Assert.StartsWith("Royal College of Physicians National Early Warning Score 2 - ", c.Code.Coding.Single().Display));
    }

    [Fact]
    public void Create_TotalIsAScoreQuantity()
    {
        var observations = Fixtures.NormalSet();
        observations.RemoveAll(o => o.Id == "hr");
        observations.Add(Fixtures.Vital("hr", "8867-4", 131, "/min", Fixtures.Now.AddMinutes(-10)));

        var quantity = Assert.IsType<Quantity>(Write(observations).Value);

        Assert.Equal(3m, quantity.Value);
        Assert.Equal("http://unitsofmeasure.org", quantity.System);
        Assert.Equal("{score}", quantity.Code);
    }

    [Fact]
    public void Create_HasSevenSubScoreComponentsWithSnomedCodes()
    {
        var observations = Fixtures.NormalSet();
        observations.RemoveAll(o => o.Id == "rr");
        observations.Add(Fixtures.Vital("rr", "9279-1", 25, "/min", Fixtures.Now.AddMinutes(-10)));

        var components = Write(observations).Component;

        var expected = new Dictionary<string, decimal>
        {
            ["1104301000000104"] = 3, // respiration rate
            ["1104311000000102"] = 0, // SpO2 scale 1
            ["1104331000000105"] = 0, // air or oxygen
            ["1104341000000101"] = 0, // systolic BP
            ["1104351000000103"] = 0, // pulse
            ["1104361000000100"] = 0, // consciousness
            ["1104371000000107"] = 0, // temperature
        };
        Assert.Equal(7, components.Count);
        foreach (var component in components)
        {
            var coding = Assert.Single(component.Code.Coding);
            Assert.Equal(Snomed, coding.System);
            Assert.Equal(expected[coding.Code!], Assert.IsType<Quantity>(component.Value).Value);
        }
    }

    [Fact]
    public void Create_Scale2UsesScale2SubScoreCode()
    {
        var options = Fixtures.Options() with
        {
            SpO2Scale = SpO2Scale.Scale2,
            Scale2Authorisation = new Scale2Authorisation("Dr Example", Fixtures.Now.AddDays(-1)),
        };

        var codes = Write(Fixtures.NormalSet(), options).Component.Select(c => c.Code.Coding.Single().Code).ToList();

        Assert.Contains("1104321000000108", codes);
        Assert.DoesNotContain("1104311000000102", codes);
    }

    [Fact]
    public void Create_DerivedFromListsEachSourceOnce()
    {
        var observation = Write(Fixtures.NormalSet());

        var references = observation.DerivedFrom.Select(r => r.Reference).ToList();
        Assert.Equal(7, references.Count);
        Assert.Equal(references.Count, references.Distinct().Count());
        Assert.Contains("Observation/sbp", references);
    }

    [Fact]
    public void Create_SourceWithoutId_Throws()
    {
        var observations = Fixtures.NormalSet();
        observations.Single(o => o.Id == "hr").Id = null;
        var (result, reading) = Calculate(observations);

        Assert.Throws<ArgumentException>(() => News2ObservationWriter.Create(result, reading, Patient, Effective));
    }
}
