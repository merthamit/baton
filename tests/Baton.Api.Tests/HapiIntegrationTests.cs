using Baton.Scenarios;
using Hl7.Fhir.Rest;

namespace Baton.Api.Tests;

// Runs only when BATON_FHIR_URL points at a FHIR server (e.g. http://localhost:8080/fhir after `docker compose up -d`).
// Without it the test returns early, so CI and machines without Docker stay green.
public class HapiIntegrationTests
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task ReloadingWithAnotherDelay_LeavesExactlyTheNewRun_AndHapiScoresEqualInMemory()
    {
        var url = Environment.GetEnvironmentVariable("BATON_FHIR_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var client = HapiVitalSignsSource.CreateClient(url);
        await WardFhirLoader.LoadAsync(client, Ward.Generate(ShiftStart, 42, treatmentDelayHours: 3));
        await WardFhirLoader.LoadAsync(client, Ward.Generate(ShiftStart, 42));

        var hapi = new WardService(new HapiVitalSignsSource(client));
        var memory = new WardService(new InMemoryVitalSignsSource(ShiftStart, 42));
        var end = ShiftStart.AddHours(23.99);

        foreach (var bed in await memory.GetWardAsync(end, CancellationToken.None))
        {
            var expected = (await memory.GetPatientAsync(bed.PatientId, end, CancellationToken.None))!.Points;
            var actual = (await hapi.GetPatientAsync(bed.PatientId, end, CancellationToken.None))!.Points;

            Assert.Equal(expected.Select(p => (p.At, p.Total, p.Risk)), actual.Select(p => (p.At, p.Total, p.Risk)));
        }

        var hapiWard = await hapi.GetWardAsync(ShiftStart.AddHours(15.5), CancellationToken.None);
        Assert.Equal(8, hapiWard.Count);

        // After two loads, every bed's chain on the server equals the generated one: nothing old is left behind.
        var source = new HapiVitalSignsSource(client);
        var generated = new InMemoryVitalSignsSource(ShiftStart, 42);
        foreach (var bed in await memory.GetWardAsync(end, CancellationToken.None))
        {
            var expected = await generated.GetEscalationAsync(bed.PatientId, end, null, CancellationToken.None);
            var actual = await source.GetEscalationAsync(bed.PatientId, end, null, CancellationToken.None);
            Assert.Equal(
                expected.OrderBy(r => r.At).ThenBy(r => r.GetType().Name),
                actual.OrderBy(r => r.At).ThenBy(r => r.GetType().Name));
        }

        Assert.Contains(await source.GetEscalationAsync("synthetic-02", end, null, CancellationToken.None), r => r is Baton.Core.Escalation.Called);
    }
}
