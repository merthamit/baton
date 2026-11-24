using Baton.Api.Shift;
using Baton.Core.Escalation;
using Baton.Core.News2;
using Baton.Scenarios;

namespace Baton.Api.Tests;

// Before the shift's start the scenario stands; from it only the visitor's entries happen (decision 21).
public class ShiftOverlayTests
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(3));
    private static readonly InMemoryVitalSignsSource Source = new(ShiftStart, 42);
    private static readonly VitalSet Set = new(24, 93, 0, 100, 112, 38.6m, Consciousness.Alert);

    private static DateTimeOffset Ward(int hour, int minute = 0) => ShiftStart.AddHours(hour).AddMinutes(minute);

    private static (WardShifts Shifts, WardShift Shift) Start(int hour)
    {
        var shifts = new WardShifts(TimeProvider.System, new BatonOptionsSnapshot(ShiftStart, 42));
        return (shifts, shifts.Start(Ward(hour), 1)!);
    }

    [Fact]
    public async Task ScenarioObservations_AfterTheStart_AreGone()
    {
        var (_, shift) = Start(14);
        var overlay = new ShiftOverlaySource(Source, shift);

        var all = await Source.GetObservationsAsync("synthetic-02", Ward(20), CancellationToken.None);
        var seen = await overlay.GetObservationsAsync("synthetic-02", Ward(20), CancellationToken.None);

        Assert.Contains(all, o => WardService.TimeOf(o) > Ward(14));
        Assert.All(seen, o => Assert.True(WardService.TimeOf(o) < Ward(14)));
        Assert.NotEmpty(seen);
    }

    [Fact]
    public async Task BedsideSet_Appears_WithItsRecordedTime()
    {
        var (shifts, shift) = Start(14);
        Assert.Null(shifts.Observe(shift, "synthetic-02", Set, null));
        var overlay = new ShiftOverlaySource(Source, shift);

        var seen = await overlay.GetObservationsAsync("synthetic-02", Ward(20), CancellationToken.None);

        var mine = seen.Where(o => WardService.TimeOf(o) == Ward(14)).ToList();
        Assert.Equal(7, mine.Count);
        Assert.All(mine, o => Assert.NotNull(o.Issued));
        Assert.All(mine, o => Assert.StartsWith($"synthetic-02-live-{shift.Id[..8]}-", o.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Records_ScenarioPastPlusShiftEntries()
    {
        var (shifts, shift) = Start(16); // bed 02's scenario call (23:19) is in the past
        shifts.Act(shift, "synthetic-02", ShiftActionKind.Concern, "Looks grey", null);
        var overlay = new ShiftOverlaySource(Source, shift);

        var records = await overlay.GetEscalationAsync("synthetic-02", Ward(23), null, CancellationToken.None);

        Assert.Contains(records, r => r is Called c && c.At < Ward(16));
        Assert.DoesNotContain(records, r => r.At >= Ward(16) && r.By != TryCourse.You);
        Assert.Contains(records, r => r is ConcernRaised { Note: "Looks grey" });
    }

    [Fact]
    public async Task Ward_FromTheOverlay_ScoresTheBedsideSet()
    {
        var (shifts, shift) = Start(14);
        shifts.Observe(shift, "synthetic-02", Set, null);
        var ward = new WardService(Source);

        var beds = await ward.GetWardAsync(Ward(14, 5), CancellationToken.None, null, new ShiftOverlaySource(Source, shift));

        var bed = beds.Single(b => b.PatientId == "synthetic-02");
        Assert.Equal(Ward(14), bed.Latest?.At);
        Assert.Equal(News2Calculator.Calculate(Set.ToNews2Input(SpO2Scale.Scale1, null)).Total, bed.Latest?.Total);
    }
}
