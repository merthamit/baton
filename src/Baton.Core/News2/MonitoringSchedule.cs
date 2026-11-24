namespace Baton.Core.News2;

/// <summary>
/// When the next full set of observations is due. One rule for the scenario generator and the API.
/// Intervals follow the RCP NEWS2 (2017) minimum monitoring frequencies, taking the strict end of a range
/// ("4–6 hourly" → 4 hours). A nurse's concern flag tightens the schedule (Baton decision 3C).
/// </summary>
public static class MonitoringSchedule
{
    /// <summary>A concern flag asks for a set within this time.</summary>
    public static readonly TimeSpan ConcernResponse = TimeSpan.FromHours(1);

    /// <summary>While a concern is active, sets are at most this far apart.</summary>
    public static readonly TimeSpan ConcernMaximumInterval = TimeSpan.FromHours(2);

    /// <summary>
    /// Minimum interval for a clinical response. An incomplete score (<c>null</c>) asks for a full set within an
    /// hour; emergency response means continuous monitoring, here at least hourly sets.
    /// </summary>
    public static TimeSpan IntervalFor(ClinicalResponse? response) => response switch
    {
        ClinicalResponse.RoutineMonitoring => TimeSpan.FromHours(12),
        ClinicalResponse.WardBasedResponse => TimeSpan.FromHours(4),
        _ => TimeSpan.FromHours(1),
    };

    /// <summary>
    /// The next set is due one interval after <paramref name="lastObservation"/>. A concern raised at or before it
    /// caps the interval at two hours; a concern raised after it makes a set due one hour after the concern, when
    /// that is earlier.
    /// </summary>
    public static DateTimeOffset NextDue(DateTimeOffset lastObservation, ClinicalResponse? response, IReadOnlyList<DateTimeOffset> concernsRaised)
    {
        ArgumentNullException.ThrowIfNull(concernsRaised);

        var interval = IntervalFor(response);
        if (concernsRaised.Any(c => c <= lastObservation) && interval > ConcernMaximumInterval)
        {
            interval = ConcernMaximumInterval;
        }

        var due = lastObservation + interval;
        var pending = concernsRaised
            .Where(c => c > lastObservation && c + ConcernResponse < due)
            .Order()
            .ToList();
        return pending.Count > 0 ? pending[0] + ConcernResponse : due;
    }
}
