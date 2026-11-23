using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir.Tests;

public class VitalSignsReaderRulesTests
{
    private static readonly DateTimeOffset Recent = Fixtures.Now.AddMinutes(-10);

    private static VitalSignsReading ReadWith(Action<List<Observation>> change, ReadOptions? options = null)
    {
        var observations = Fixtures.NormalSet();
        change(observations);
        return VitalSignsReader.Read(observations, options ?? Fixtures.Options());
    }

    private static void Replace(List<Observation> observations, string id, Observation replacement)
    {
        observations.RemoveAll(o => o.Id == id);
        observations.Add(replacement);
    }

    [Fact]
    public void Stale_IsNotUsed()
    {
        var reading = ReadWith(o => Replace(o, "hr", Fixtures.Vital("hr", "8867-4", 70, "/min", Fixtures.Now.AddHours(-5))));

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("older than"));
    }

    [Fact]
    public void InTheFuture_IsNotUsed()
    {
        var reading = ReadWith(o => Replace(o, "hr", Fixtures.Vital("hr", "8867-4", 70, "/min", Fixtures.Now.AddMinutes(5))));

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("future"));
    }

    [Fact]
    public void WithoutTime_IsNotUsed()
    {
        var reading = ReadWith(o => o.Single(x => x.Id == "hr").Effective = null);

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("no usable time"));
    }

    [Fact]
    public void DataAbsentReason_IsNotUsed()
    {
        var reading = ReadWith(o =>
        {
            var hr = o.Single(x => x.Id == "hr");
            hr.Value = null;
            hr.DataAbsentReason = new CodeableConcept("http://terminology.hl7.org/CodeSystem/data-absent-reason", "not-performed");
        });

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("not measured"));
    }

    [Fact]
    public void WithoutValue_IsNotUsed()
    {
        var reading = ReadWith(o => o.Single(x => x.Id == "hr").Value = null);

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("no value"));
    }

    [Fact]
    public void ComparatorValue_IsNotUsed()
    {
        var reading = ReadWith(o => ((Quantity)o.Single(x => x.Id == "hr").Value!).Comparator = Quantity.QuantityComparator.GreaterThan);

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("not an exact"));
    }

    [Fact]
    public void WrongUnit_IsNotUsed()
    {
        var reading = ReadWith(o => Replace(o, "rr", Fixtures.Vital("rr", "9279-1", 16, "%", Recent)));

        Assert.Null(reading.Input.RespirationRate);
        Assert.Contains(reading.Notes, n => n.Contains("RespirationRate") && n.Contains("unit"));
    }

    [Fact]
    public void NewestBroken_DoesNotFallBackToOlderValidReading()
    {
        var reading = ReadWith(o =>
        {
            o.Add(Fixtures.Vital("hr-old", "8867-4", 70, "/min", Fixtures.Now.AddHours(-1)));
            o.Single(x => x.Id == "hr").Value = null;
        });

        Assert.Null(reading.Input.PulseRate);
    }

    [Fact]
    public void NewestIsChosenAcrossTimeZones()
    {
        // 10:50+03:00 is 07:50Z, newer than 07:45Z.
        var reading = ReadWith(o =>
        {
            Replace(o, "hr", Fixtures.Vital("hr-z", "8867-4", 70, "/min", new DateTimeOffset(2026, 11, 23, 7, 45, 0, TimeSpan.Zero)));
            o.Add(Fixtures.Vital("hr-tr", "8867-4", 120, "/min", new DateTimeOffset(2026, 11, 23, 10, 50, 0, TimeSpan.FromHours(3))));
        });

        Assert.Equal(120, reading.Input.PulseRate);
        Assert.Equal("hr-tr", reading.Sources[News2Parameter.PulseRate].Id);
    }

    [Fact]
    public void Systolic_IsReadFromBloodPressurePanel()
    {
        var reading = ReadWith(o => Replace(o, "sbp", Fixtures.BloodPressurePanel("bp", 132, 80, Recent)));

        Assert.Equal(132, reading.Input.SystolicBp);
        Assert.Equal("bp", reading.Sources[News2Parameter.SystolicBp].Id);
    }

    [Fact]
    public void Systolic_PanelAndStandalone_NewestWins()
    {
        var reading = ReadWith(o => o.Add(Fixtures.BloodPressurePanel("bp", 95, 60, Fixtures.Now.AddMinutes(-2))));

        Assert.Equal(95, reading.Input.SystolicBp);
    }

    [Theory]
    [InlineData("3151-8", 2, "L/min", true)]
    [InlineData("3151-8", 0, "L/min", false)]
    [InlineData("3150-0", 28, "%", true)]
    [InlineData("3150-0", 21, "%", false)]
    public void Oxygen_IsReadFromFlowOrInspiredConcentration(string loinc, decimal value, string unit, bool expected)
    {
        var reading = ReadWith(o => Replace(o, "o2flow", Fixtures.Vital("o2", loinc, value, unit, Recent)));

        Assert.Equal(expected, reading.Input.OnSupplementalOxygen);
    }

    [Theory]
    [InlineData("LA9340-6", Consciousness.Alert)]
    [InlineData("LA6560-2", Consciousness.Confusion)]
    [InlineData("LA17108-4", Consciousness.Voice)]
    [InlineData("LA17107-6", Consciousness.Pain)]
    [InlineData("LA9343-0", Consciousness.Unresponsive)]
    public void Consciousness_IsReadFromLoincAnswers(string answer, Consciousness expected)
    {
        var reading = ReadWith(o => Replace(o, "acvpu", Fixtures.Consciousness("acvpu", answer, Recent)));

        Assert.Equal(expected, reading.Input.Consciousness);
        Assert.Equal(ConfusionOnset.Unknown, reading.Input.ConfusionOnset);
    }

    [Fact]
    public void Consciousness_UnknownAnswer_IsNotUsed()
    {
        var reading = ReadWith(o => Replace(o, "acvpu", Fixtures.Consciousness("acvpu", "LA25161-3", Recent)));

        Assert.Null(reading.Input.Consciousness);
        Assert.Contains(reading.Notes, n => n.Contains("Consciousness"));
    }

    [Fact]
    public void Consciousness_IsAlsoReadFromLevelOfConsciousnessCode()
    {
        var reading = ReadWith(o => Replace(o, "acvpu", Fixtures.Consciousness("loc", "LA17108-4", Recent, loinc: "80288-4")));

        Assert.Equal(Consciousness.Voice, reading.Input.Consciousness);
    }

    [Fact]
    public void Fahrenheit_IsConvertedToCelsiusAndNoted()
    {
        var reading = ReadWith(o => Replace(o, "temp", Fixtures.Vital("temp", "8310-5", 98.6m, "[degF]", Recent)));

        Assert.Equal(37.0m, reading.Input.TemperatureCelsius);
        Assert.Contains(reading.Notes, n => n.Contains("°F"));
    }

    [Fact]
    public void DecimalRespirationRate_IsRoundedHalfUpAndNoted()
    {
        var reading = ReadWith(o => Replace(o, "rr", Fixtures.Vital("rr", "9279-1", 16.5m, "/min", Recent)));

        Assert.Equal(17, reading.Input.RespirationRate);
        Assert.Contains(reading.Notes, n => n.Contains("RespirationRate") && n.Contains("rounded"));
    }

    [Fact]
    public void Scale2DecisionFromOptions_IsPassedToInput()
    {
        var decision = new Scale2Authorisation("Dr Example", Fixtures.Now.AddDays(-1));
        var options = Fixtures.Options() with { SpO2Scale = SpO2Scale.Scale2, Scale2Authorisation = decision };

        var reading = VitalSignsReader.Read(Fixtures.NormalSet(), options);

        Assert.Equal(SpO2Scale.Scale2, reading.Input.SpO2Scale);
        Assert.Equal(decision, reading.Input.Scale2Authorisation);
    }
}
