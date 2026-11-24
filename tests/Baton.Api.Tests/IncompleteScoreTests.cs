using Hl7.Fhir.Model;

namespace Baton.Api.Tests;

// When a parameter is missing there is no total; the card shows "at least N" from MinimumTotal.
public class IncompleteScoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    /// <summary>The in-memory ward without any SpO2 readings.</summary>
    private sealed class WithoutSpO2(IVitalSignsSource inner) : IVitalSignsSource
    {
        public System.Threading.Tasks.Task<IReadOnlyList<PatientRef>> GetPatientsAsync(CancellationToken cancellationToken) =>
            inner.GetPatientsAsync(cancellationToken);

        public async System.Threading.Tasks.Task<IReadOnlyList<Observation>> GetObservationsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken) =>
            (await inner.GetObservationsAsync(patientId, until, cancellationToken))
                .Where(o => !o.Code.Coding.Any(c => c.Code is "2708-6" or "59408-5"))
                .ToList();

        public System.Threading.Tasks.Task<PatientEvents> GetEventsAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken) =>
            inner.GetEventsAsync(patientId, until, cancellationToken);

        public System.Threading.Tasks.Task<IReadOnlyList<Baton.Core.Escalation.EscalationRecord>> GetEscalationAsync(string patientId, DateTimeOffset until, CancellationToken cancellationToken) =>
            inner.GetEscalationAsync(patientId, until, cancellationToken);
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingSpO2_NoTotal_MinimumTotalAndHourlyDue()
    {
        var service = new WardService(new WithoutSpO2(new InMemoryVitalSignsSource(Start, 42)));

        var bed = (await service.GetWardAsync(Start.AddHours(15.5), default)).Single(b => b.Bed == "02");

        Assert.Null(bed.Latest!.Total);
        Assert.NotNull(bed.Latest.MinimumTotal);
        Assert.Contains(bed.Latest.Problems, p => p.Parameter == Baton.Core.News2.News2Parameter.SpO2);
        Assert.Equal(bed.Latest.At.AddHours(1), bed.NextDue);
    }
}
