using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir.Tests;

public class VitalSignsReaderTests
{
    [Fact]
    public void Read_NormalSet_GivesCompleteZeroScoreWithAllSources()
    {
        var reading = VitalSignsReader.Read(Fixtures.NormalSet(), Fixtures.Options());

        var result = News2Calculator.Calculate(reading.Input);

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.Total);
        Assert.Equal(7, reading.Sources.Count);
        Assert.Equal("sbp", reading.Sources[News2Parameter.SystolicBp].Id);
    }

    [Fact]
    public void Read_NoObservations_LeavesEveryFieldEmptyAndDoesNotAssumeRoomAir()
    {
        var reading = VitalSignsReader.Read([], Fixtures.Options());

        Assert.Null(reading.Input.RespirationRate);
        Assert.Null(reading.Input.SpO2);
        Assert.Null(reading.Input.OnSupplementalOxygen);
        Assert.Null(reading.Input.SystolicBp);
        Assert.Null(reading.Input.PulseRate);
        Assert.Null(reading.Input.Consciousness);
        Assert.Null(reading.Input.TemperatureCelsius);
        Assert.Empty(reading.Sources);
    }

    [Fact]
    public void Read_EnteredInError_IsIgnored()
    {
        var observations = Fixtures.NormalSet();
        observations.Single(o => o.Id == "hr").Status = ObservationStatus.EnteredInError;

        var reading = VitalSignsReader.Read(observations, Fixtures.Options());

        Assert.Null(reading.Input.PulseRate);
        Assert.False(reading.Sources.ContainsKey(News2Parameter.PulseRate));
    }
}
