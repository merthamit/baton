using Baton.Scenarios;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

namespace Baton.Api.Tests;

public class SourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));

    private static DateTimeOffset At(double hour) => ShiftSimulator.AtMinute(Start, hour);

    [Fact]
    public async System.Threading.Tasks.Task InMemory_PatientHasBirthDateAndDiagnosis()
    {
        var patients = await new InMemoryVitalSignsSource(Start, 42).GetPatientsAsync(default);
        var ayse = patients.Single(p => p.Bed == "02");

        Assert.NotNull(ayse.BirthDate);
        Assert.Equal(Ward.Generate(Start, 42)[1].Diagnosis, ayse.Diagnosis);
    }

    [Fact]
    public async System.Threading.Tasks.Task InMemory_Events_UpToUntil_InClockTime()
    {
        var source = new InMemoryVitalSignsSource(Start, 42);

        var later = await source.GetEventsAsync("synthetic-02", At(12.5), default);
        var earlier = await source.GetEventsAsync("synthetic-02", At(12), default);

        Assert.True(later.Available);
        Assert.Contains(later.Events, e => e.Kind == ScenarioEventKind.Concern && e.At == At(12.25));
        Assert.DoesNotContain(earlier.Events, e => e.Kind == ScenarioEventKind.Concern);
        Assert.All(later.Events, e => Assert.True(e.At <= At(12.5)));
    }

    [Fact]
    public async System.Threading.Tasks.Task InMemory_EventsOfAnEarlierStartedCard_KeepTheirShiftedTimes()
    {
        // Bed 07 is the sepsis card started 6 hours earlier: concern at 12.25 - 6 = 6.25 (14:15).
        var events = await new InMemoryVitalSignsSource(Start, 42).GetEventsAsync("synthetic-07", At(24), default);

        Assert.Contains(events.Events, e => e.Kind == ScenarioEventKind.Concern && e.At == At(6.25));
    }

    [Fact]
    public async System.Threading.Tasks.Task InMemory_Delay_ChangesEvents()
    {
        static async Task<DateTimeOffset> Arrived(double delay) =>
            (await new InMemoryVitalSignsSource(Start, 42, delay).GetEventsAsync("synthetic-02", At(24), default))
                .Events.First(e => e.Kind == ScenarioEventKind.DoctorArrived).At;

        Assert.True(await Arrived(3) > await Arrived(0));
    }

    [Fact]
    public async System.Threading.Tasks.Task InMemory_ThirtyBeds()
    {
        Assert.Equal(30, (await new InMemoryVitalSignsSource(Start, 42, beds: 30).GetPatientsAsync(default)).Count);
    }

    [Fact]
    public void FromFhir_ReadsBirthDateAndConditionText()
    {
        var patient = new Patient { Id = "p1", BirthDate = "1955-08-15", Name = [new HumanName { Text = "Test" }] };
        var condition = new Condition { Code = new CodeableConcept { Text = "Community-acquired pneumonia" } };

        var reference = PatientRef.FromFhir(patient, condition);

        Assert.Equal(new DateOnly(1955, 8, 15), reference.BirthDate);
        Assert.Equal("Community-acquired pneumonia", reference.Diagnosis);
    }

    [Fact]
    public async System.Threading.Tasks.Task Hapi_EventsUnavailable()
    {
        var events = await new HapiVitalSignsSource(new FhirClient("http://127.0.0.1:9/fhir")).GetEventsAsync("synthetic-02", Start, default);

        Assert.False(events.Available);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async System.Threading.Tasks.Task InMemory_Escalation_EqualsScenarioRecords()
    {
        var until = At(17); // 01:00
        var expected = ScenarioEscalation.From(Ward.Generate(Start, 42)[1], Start).Where(r => r.At <= until);

        var actual = await new InMemoryVitalSignsSource(Start, 42).GetEscalationAsync("synthetic-02", until, null, default);

        Assert.Equal(
            expected.OrderBy(r => r.At).ThenBy(r => r.GetType().Name),
            actual.OrderBy(r => r.At).ThenBy(r => r.GetType().Name));
    }
}
