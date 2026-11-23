using System.Globalization;
using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir.Tests;

// Regression tests for the findings of the week 2 whole-branch review.
public class ReviewFindingTests
{
    private static readonly DateTimeOffset Recent = Fixtures.Now.AddMinutes(-10);

    [Fact]
    public void TiedNewestReadingsWithDifferentValues_AreNotUsedWhateverTheOrder()
    {
        foreach (var flip in new[] { false, true })
        {
            var observations = Fixtures.NormalSet();
            observations.RemoveAll(o => o.Id == "hr");
            var a = Fixtures.Vital("hr-a", "8867-4", 70, "/min", Recent);
            var b = Fixtures.Vital("hr-b", "8867-4", 135, "/min", Recent);
            observations.AddRange(flip ? [b, a] : [a, b]);

            var reading = VitalSignsReader.Read(observations, Fixtures.Options());

            Assert.Null(reading.Input.PulseRate);
            Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("conflicting"));
        }
    }

    [Fact]
    public void TiedNewestReadingsWithTheSameValue_AreUsed()
    {
        var observations = Fixtures.NormalSet();
        observations.Add(Fixtures.Vital("hr-copy", "8867-4", 70, "/min", Recent));

        var reading = VitalSignsReader.Read(observations, Fixtures.Options());

        Assert.Equal(70, reading.Input.PulseRate);
    }

    [Fact]
    public void ReadingWithOnlyADate_BlocksTheParameterInsteadOfFallingBackToAnOlderReading()
    {
        var observations = Fixtures.NormalSet();
        observations.RemoveAll(o => o.Id == "hr");
        observations.Add(Fixtures.Vital("hr-old", "8867-4", 70, "/min", Fixtures.Now.AddHours(-1)));
        var dateOnly = Fixtures.Vital("hr-date", "8867-4", 135, "/min", Recent);
        dateOnly.Effective = new FhirDateTime("2026-11-23");
        observations.Add(dateOnly);

        var reading = VitalSignsReader.Read(observations, Fixtures.Options());

        Assert.Null(reading.Input.PulseRate);
        Assert.Contains(reading.Notes, n => n.Contains("PulseRate") && n.Contains("no usable time"));
    }

    [Fact]
    public void Writer_SourceForAnotherPatient_Throws()
    {
        var observations = Fixtures.NormalSet();
        observations.Single(o => o.Id == "spo2").Subject = new ResourceReference("Patient/someone-else");
        var reading = VitalSignsReader.Read(observations, Fixtures.Options());
        var result = News2Calculator.Calculate(reading.Input);

        Assert.Throws<ArgumentException>(() =>
            News2ObservationWriter.Create(result, reading, new ResourceReference(Fixtures.PatientReference), Fixtures.Now));
    }

    [Fact]
    public void Writer_SourceWithoutSubject_Throws()
    {
        var observations = Fixtures.NormalSet();
        observations.Single(o => o.Id == "spo2").Subject = null;
        var reading = VitalSignsReader.Read(observations, Fixtures.Options());
        var result = News2Calculator.Calculate(reading.Input);

        Assert.Throws<ArgumentException>(() =>
            News2ObservationWriter.Create(result, reading, new ResourceReference(Fixtures.PatientReference), Fixtures.Now));
    }

    [Fact]
    public void Notes_UseInvariantCultureOnATurkishMachine()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        try
        {
            var observations = Fixtures.NormalSet();
            observations.RemoveAll(o => o.Id == "rr");
            observations.Add(Fixtures.Vital("rr", "9279-1", 16.5m, "/min", Recent));

            var reading = VitalSignsReader.Read(observations, Fixtures.Options());

            Assert.Contains("RespirationRate: 16.5 rounded to 17.", reading.Notes);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
