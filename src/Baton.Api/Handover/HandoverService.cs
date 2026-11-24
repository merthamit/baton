using Baton.Core.News2;

namespace Baton.Api.Handover;

/// <summary>One bed in the handover list: the SBAR, the latest confirmed handover and whether data arrived after it.</summary>
public sealed record HandoverRow(string PatientId, string? Bed, string Name, Sbar Sbar, ConfirmedHandover? Confirmed, bool NewDataSinceHandover);

/// <summary>The ward's handover at a moment. <see cref="CanConfirm"/> is false in Try mode (a rehearsal is not stored).</summary>
public sealed record HandoverResponse(DateTimeOffset At, DateTimeOffset WindowStart, bool CanConfirm, int ConfirmedCount, IReadOnlyList<HandoverRow> Rows);

public enum ConfirmOutcome
{
    Created,
    UnknownPatient,
    AlreadyTakenOver,
}

/// <summary>Builds the ranked handover list and stores a confirmed handover as a FHIR document (decision 15C).</summary>
public sealed class HandoverService(WardService ward, IVitalSignsSource source, IHandoverStore store, DateTimeOffset shiftStart)
{
    public async Task<HandoverResponse> GetAsync(DateTimeOffset at, string? language, IVitalSignsSource? overlay, bool canConfirm, CancellationToken cancellationToken)
    {
        var rows = new List<HandoverRow>();
        foreach (var patient in await (overlay ?? source).GetPatientsAsync(cancellationToken))
        {
            if (await ward.GetSbarInputAsync(patient.Id, at, shiftStart, language, overlay, cancellationToken) is not { } built)
            {
                continue;
            }

            var sbar = SbarBuilder.Build(built.Input, language);
            var confirmed = await store.LatestAsync(patient.Id, at, cancellationToken);
            var newData = confirmed is not null
                && (built.Input.Points.Any(p => p.At > confirmed.At && p.At <= at) || built.Input.Records.Any(r => r.At > confirmed.At && r.At <= at));
            rows.Add(new HandoverRow(patient.Id, patient.Bed, patient.Name, sbar, confirmed, newData));
        }

        var windowStart = WindowStart(at);
        return new HandoverResponse(at, windowStart, canConfirm, rows.Count(r => r.Confirmed is { } c && c.At >= windowStart), Order(rows));
    }

    public async Task<(ConfirmOutcome Outcome, ConfirmedHandover? Handover)> ConfirmAsync(string patientId, DateTimeOffset at, string? language, CancellationToken cancellationToken)
    {
        if (await ward.GetSbarInputAsync(patientId, at, shiftStart, language, null, cancellationToken) is not { } built)
        {
            return (ConfirmOutcome.UnknownPatient, null);
        }

        var document = HandoverDocument.Build(SbarBuilder.Build(built.Input, language), built.Snapshot);
        return await store.TryAddAsync(patientId, document, HandoverDocument.ProvenanceFor(document), cancellationToken)
            ? (ConfirmOutcome.Created, new ConfirmedHandover(document.Id!, at))
            : (ConfirmOutcome.AlreadyTakenOver, null);
    }

    public DateTimeOffset WindowStart(DateTimeOffset at)
    {
        var start = at.AddHours(-SbarBuilder.WindowHours);
        return (start < shiftStart ? shiftStart : start).ToOffset(at.Offset);
    }

    /// <summary>
    /// Sickest first: current risk (High, Medium, Low–medium, Low, unscored last); within a risk, rows with missing items
    /// first, then the higher current score, then the higher peak of the window, then the bed.
    /// </summary>
    public static IReadOnlyList<HandoverRow> Order(IEnumerable<HandoverRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows
            .OrderByDescending(r => Rank(r.Sbar.CurrentRisk))
            .ThenByDescending(r => r.Sbar.Missing.Count > 0)
            .ThenByDescending(r => r.Sbar.CurrentTotal ?? -1)
            .ThenByDescending(r => r.Sbar.PeakTotal ?? -1)
            .ThenBy(r => r.Bed, StringComparer.Ordinal)
            .ToList();
    }

    private static int Rank(RiskLevel? risk) => risk switch
    {
        RiskLevel.High => 4,
        RiskLevel.Medium => 3,
        RiskLevel.LowMedium => 2,
        RiskLevel.Low => 1,
        _ => 0,
    };
}
