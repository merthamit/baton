#nullable disable warnings
using Baton.Core.News2;
using Baton.Fhir;
using Hl7.Fhir.Model;

namespace Baton.Scenarios.Tests;

// CLI "observe": one new synthetic observation set for a bed, written to the FHIR server so the live board's
// Subscription fires. Values are checked before anything is written.
public class ObserveCommandTests
{
    private static readonly string[] Full =
    [
        "--bed", "02", "--at", "2026-11-23T15:10:00+03:00", "--rr", "26", "--spo2", "91", "--o2", "2",
        "--sbp", "95", "--hr", "118", "--temp", "38.9", "--acvpu", "A", "--fhir", "http://localhost:8080/fhir",
    ];

    [Fact]
    public void ParsesAFullSet()
    {
        var (request, error) = ObserveCommand.Parse(Full);

        Assert.Null(error);
        Assert.Equal("synthetic-02", request.PatientId);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 15, 10, 0, TimeSpan.FromHours(3)), request.At);
        Assert.Equal(new VitalSet(26, 91, 2m, 95, 118, 38.9m, Consciousness.Alert), request.Vitals);
        Assert.Equal("http://localhost:8080/fhir", request.FhirBaseUrl);
    }

    [Theory]
    [InlineData("--rr", "2")]
    [InlineData("--spo2", "101")]
    [InlineData("--temp", "50")]
    [InlineData("--acvpu", "X")]
    [InlineData("--bed", "two")]
    public void RejectsOutOfRangeValues(string option, string value)
    {
        var args = Full.ToArray();
        args[Array.IndexOf(args, option) + 1] = value;

        var (request, error) = ObserveCommand.Parse(args);

        Assert.Null(request);
        Assert.Contains(option, error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAtWithoutOffset()
    {
        var args = Full.ToArray();
        args[Array.IndexOf(args, "--at") + 1] = "2026-11-23T15:10:00";

        var (request, error) = ObserveCommand.Parse(args);

        Assert.Null(request);
        Assert.Contains("--at", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleHoldsSevenObservationsThatScore()
    {
        var request = ObserveCommand.Parse(Full).Request;
        var bundle = ObserveCommand.ToBundle(request);

        var observations = bundle.Entry.Select(e => e.Resource).OfType<Observation>().ToList();
        Assert.Equal(Bundle.BundleType.Transaction, bundle.Type);
        Assert.Equal(7, observations.Count);
        Assert.All(observations, o => Assert.StartsWith("synthetic-02-live-202611231510-", o.Id, StringComparison.Ordinal));
        Assert.All(bundle.Entry, e => Assert.Equal(Bundle.HTTPVerb.PUT, e.Request.Method));

        var reading = VitalSignsReader.Read(observations, new ReadOptions(request.At, TimeSpan.FromHours(1)));
        var expected = News2Calculator.Calculate(request.Vitals.ToNews2Input(SpO2Scale.Scale1, null));
        Assert.Equal(expected.Total, News2Calculator.Calculate(reading.Input).Total);
        Assert.Equal(13, expected.Total); // RR 26 →3, SpO2 91 →3, oxygen →2, SBP 95 →2, HR 118 →2, temp 38.9 →1
    }
}
