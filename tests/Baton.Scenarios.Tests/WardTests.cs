using Baton.Core.News2;
using Baton.Fhir;

namespace Baton.Scenarios.Tests;

public class WardTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;

    [Fact]
    public void Ward_HasEightBedsWithDistinctPatients()
    {
        var ward = Ward.Generate(Start, seed: 42);

        Assert.Equal(8, ward.Count);
        Assert.Equal(8, ward.Select(b => b.Bed).Distinct().Count());
        Assert.Equal(8, ward.Select(b => b.PatientName).Distinct().Count());
    }

    [Fact]
    public void Ward_SameSeedSameWard()
    {
        static IEnumerable<object> Shape(IReadOnlyList<WardBed> w) =>
            w.SelectMany(b => b.Patient.Measurements.Select(m => (object)(b.Bed, m.Hour, m.Vitals)));

        Assert.Equal(Shape(Ward.Generate(Start, 7)), Shape(Ward.Generate(Start, 7)));
        Assert.NotEqual(Shape(Ward.Generate(Start, 7)), Shape(Ward.Generate(Start, 8)));
    }

    [Fact]
    public void ShiftedCopy_MovesItsEventsToo()
    {
        // Y-07 is the sepsis card started 6 hours earlier: concern at 12.25 - 6 = 6.25 (14:15).
        var y07 = Ward.Generate(Start, 42, noiseLevel: 0).Single(b => b.Bed == "Y-07");

        Assert.Equal(6.25, y07.Patient.Events.Single(e => e.Kind == ScenarioEventKind.Concern).Hour, 3);
    }

    [Fact]
    public void ShiftedCopy_DropsEventsAfterTheShift()
    {
        var late = CardShift.Shift(ScenarioCards.Sepsis, 10);

        Assert.All(late.Events, e => Assert.True(e.Hour < ShiftSimulator.ShiftHours));
        Assert.Equal(ScenarioCards.Sepsis.Delay!.TreatmentHour + 10, late.Delay!.TreatmentHour, 3);
    }

    [Fact]
    public void EveryMeasurement_RoundTripsThroughFhirWithTheSameScore()
    {
        foreach (var bed in Ward.Generate(Start, 42))
        {
            foreach (var m in bed.Patient.Measurements)
            {
                var observations = ScenarioFhirExport.ToObservations(bed, m);
                var options = new ReadOptions(m.At, TimeSpan.FromHours(1))
                {
                    SpO2Scale = m.Input.SpO2Scale,
                    Scale2Authorisation = m.Input.Scale2Authorisation,
                };

                var reading = VitalSignsReader.Read(observations, options);
                var score = News2Calculator.Calculate(reading.Input);

                Assert.True(score.IsComplete, $"{bed.Bed} {m.Hour}: {string.Join("; ", reading.Notes)}");
                Assert.Equal(m.Score.Total, score.Total);
            }
        }
    }

    [Fact]
    public void FhirObservations_HaveUniqueIdsAndTheBedsPatient()
    {
        var bed = Ward.Generate(Start, 42)[1];
        var all = bed.Patient.Measurements.SelectMany(m => ScenarioFhirExport.ToObservations(bed, m)).ToList();

        Assert.Equal(all.Count, all.Select(o => o.Id).Distinct().Count());
        Assert.All(all, o => Assert.Equal($"Patient/{bed.PatientId}", o.Subject!.Reference));
    }
}
