using Baton.Api.Handover;
using Baton.Core.Escalation;
using Baton.Core.News2;

namespace Baton.Api.Tests;

// The rule-based SBAR: every sentence from the record, every sentence with a source, nothing invented.
// Ward time +03:00; the handover moment is 08:00 the morning after the scenario's 08:00 start.
public class SbarBuilderTests
{
    private static readonly TimeSpan Ward = TimeSpan.FromHours(3);
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 23, 8, 0, 0, Ward);
    private static readonly DateTimeOffset Handover = new(2026, 11, 24, 8, 0, 0, Ward);
    private const string Pid = "synthetic-02";
    private const string Nurse = "Practitioner/synthetic-nurse";

    private static DateTimeOffset T(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, Ward);

    private static readonly PatientRef Patient = new(Pid, "Ayşe K.", "02", SpO2Scale.Scale1, null, new DateOnly(1955, 8, 15), "Urinary tract infection");

    internal static ScorePoint Point(
        DateTimeOffset at,
        int? total,
        RiskLevel? risk,
        IReadOnlyDictionary<News2Parameter, int?>? subScores = null,
        News2Input? input = null,
        IReadOnlyList<News2Problem>? problems = null) =>
        new(at, total, total, risk, null, null,
            subScores ?? new Dictionary<News2Parameter, int?>(),
            [], problems ?? [], input ?? new News2Input(), [],
            [$"Observation/obs-{Pid}-{at:yyyyMMddHHmm}-rr", $"Observation/obs-{Pid}-{at:yyyyMMddHHmm}-hr"]);

    internal static SbarInput Input(
        IReadOnlyList<ScorePoint> points,
        IReadOnlyList<EscalationRecord>? records = null,
        DateTimeOffset? at = null,
        DateTimeOffset? nextDue = null,
        int? overdue = null,
        EscalationOptions? options = null) =>
        new(Patient, 71, points, records ?? [], at ?? Handover, ShiftStart, nextDue, overdue, options ?? new EscalationOptions());

    private static SbarSection Section(Sbar sbar, SbarPart part) => sbar.Sections.Single(s => s.Part == part);

    private static IReadOnlyList<string> Texts(Sbar sbar, SbarPart part) => Section(sbar, part).Sentences.Select(s => s.Text).ToList();

    [Fact]
    public void SituationGivesCurrentScoreRiskAndTime()
    {
        var latest = Point(T(24, 7), 3, RiskLevel.Low);
        var sbar = SbarBuilder.Build(Input([latest]), null);

        var first = Section(sbar, SbarPart.Situation).Sentences[0];
        Assert.Equal("NEWS2 3 Low at 07:00.", first.Text);
        Assert.Equal(latest.Sources, first.Sources.Select(s => s.Reference));
        Assert.All(first.Sources, s => Assert.Equal(T(24, 7), s.At));
    }

    [Fact]
    public void SituationAddsTheWindowPeakWhenHigherThanNow()
    {
        var sbar = SbarBuilder.Build(Input([Point(T(24, 0), 6, RiskLevel.Medium), Point(T(24, 7), 3, RiskLevel.Low)]), null);

        Assert.Contains("Highest this shift: 6 Medium at 00:00.", Texts(sbar, SbarPart.Situation));
        Assert.Equal(6, sbar.PeakTotal);
        Assert.Equal(3, sbar.CurrentTotal);
    }

    [Fact]
    public void SituationOmitsPeakWhenItEqualsCurrent()
    {
        var sbar = SbarBuilder.Build(Input([Point(T(24, 0), 2, RiskLevel.Low), Point(T(24, 7), 3, RiskLevel.Low)]), null);

        Assert.DoesNotContain(Texts(sbar, SbarPart.Situation), t => t.StartsWith("Highest", StringComparison.Ordinal));
    }

    [Fact]
    public void SituationNamesTheDrivers()
    {
        var subScores = new Dictionary<News2Parameter, int?> { [News2Parameter.RespirationRate] = 2, [News2Parameter.PulseRate] = 1, [News2Parameter.SpO2] = 0 };
        var input = new News2Input { RespirationRate = 21, PulseRate = 96, SpO2 = 97 };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 3, RiskLevel.Low, subScores, input)]), null);

        Assert.Contains("Driven by: Resp 21, Pulse 96.", Texts(sbar, SbarPart.Situation));
    }

    [Fact]
    public void SituationReportsAnIncompleteLatestSet()
    {
        var problems = new List<News2Problem> { new(News2Parameter.Temperature, ProblemCode.Missing, "not measured") };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), null, null, problems: problems)]), null);

        Assert.Contains("Latest set incomplete at 07:00: Temp.", Texts(sbar, SbarPart.Situation));
    }

    [Fact]
    public void PointsBeforeTheWindowAreIgnored()
    {
        // 19:00 the day before is 13 hours before the 08:00 handover: outside the 12-hour window.
        var sbar = SbarBuilder.Build(Input([Point(T(23, 19), 7, RiskLevel.High), Point(T(24, 7), 3, RiskLevel.Low)]), null);

        Assert.Equal(3, sbar.PeakTotal);
        Assert.Equal(T(23, 20), sbar.WindowStart);
    }

    [Fact]
    public void BackgroundGivesAgeDiagnosisAndConcerns()
    {
        var records = new EscalationRecord[]
        {
            new ConcernRaised(T(23, 20, 15), Nurse, "Restless, not eating"),
            new PatientOrFamilyConcern(T(23, 21), "RelatedPerson/synthetic-family", "She is not herself"),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 3, RiskLevel.Low)], records), null);

        var background = Section(sbar, SbarPart.Background).Sentences;
        Assert.Equal("71, Urinary tract infection.", background[0].Text);
        Assert.Contains(background[0].Sources, s => s.Reference == $"Patient/{Pid}");
        Assert.Contains(background[0].Sources, s => s.Reference == $"Condition/condition-{Pid}");
        Assert.Equal("Nurse concern at 20:15: Restless, not eating.", background[1].Text);
        Assert.Equal($"Flag/concern-{Pid}-202611232015", background[1].Sources.Single().Reference);
        Assert.Equal("Patient/family concern at 21:00: She is not herself.", background[2].Text);
        Assert.Equal($"Communication/voice-{Pid}-202611232100", background[2].Sources.Single().Reference);
    }

    [Fact]
    public void AssessmentListsEachCallStepWithItsTask()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(23, 23, 19), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Arrived(T(23, 23, 40), "Practitioner/synthetic-oncall", "call-1", false),
            new Assessed(T(23, 23, 45), "Practitioner/synthetic-oncall", "call-1", "Suspected sepsis"),
            new Planned(T(24, 0, 5), "Practitioner/synthetic-oncall", "call-1", "Cultures, fluids, antibiotics."),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 3, RiskLevel.Low)], records), null);

        var assessment = Section(sbar, SbarPart.Assessment).Sentences;
        Assert.Equal(
            ["On-call doctor called at 23:19.", "Arrived at 23:40.", "Assessment: Suspected sepsis.", "Plan: Cultures, fluids, antibiotics."],
            assessment.Select(s => s.Text));
        Assert.All(assessment, s => Assert.Equal("Task/call-1", s.Sources.Single().Reference));
    }

    [Fact]
    public void ArrivalMarkedSimulatedWhenRecordSaysSo()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(24, 0, 20), Nurse, "call-2", EscalationTier.CriticalCare),
            new Arrived(T(24, 0, 30), "Practitioner/synthetic-cct", "call-2", true),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 3, RiskLevel.Low)], records), null);

        Assert.Contains("Arrived at 00:30 (simulated).", Texts(sbar, SbarPart.Assessment));
    }

    [Fact]
    public void OpenCallFromBeforeWindowIsShown()
    {
        var records = new EscalationRecord[] { new Called(T(23, 19, 30), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 5, RiskLevel.Medium)], records), null);

        Assert.Contains("On-call doctor called at 19:30.", Texts(sbar, SbarPart.Assessment));
    }

    [Fact]
    public void ClosedCallFromBeforeWindowIsNotShown()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(23, 18), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Arrived(T(23, 18, 30), "Practitioner/synthetic-oncall", "call-1", false),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 1, RiskLevel.Low)], records), null);

        Assert.Equal(["No escalation this shift."], Texts(sbar, SbarPart.Assessment));
    }

    [Fact]
    public void EpisodeCallFromBeforeWindowIsShownWhenScoresStayRaised()
    {
        // Bed 07's evening: critical care came at 18:30, before the 20:00 window; the 8 at 20:15 is the same episode.
        var records = new EscalationRecord[]
        {
            new Called(T(23, 18, 20), Nurse, "call-2", EscalationTier.CriticalCare),
            new Arrived(T(23, 18, 30), "Practitioner/synthetic-cct", "call-2", true),
        };
        var points = new[] { Point(T(23, 20, 15), 8, RiskLevel.High), Point(T(24, 7, 15), 2, RiskLevel.Low) };
        var sbar = SbarBuilder.Build(Input(points, records), null);

        Assert.Equal(["Critical care team called at 18:20.", "Arrived at 18:30 (simulated)."], Texts(sbar, SbarPart.Assessment));
        Assert.DoesNotContain(sbar.Missing, m => m.Kind is MissingKind.NoCallAfterScore or MissingKind.NoCriticalCareCall);
    }

    [Fact]
    public void StepsAfterAtAreNotShown()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(24, 7, 30), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Arrived(T(24, 7, 50), "Practitioner/synthetic-oncall", "call-1", false),
            new Planned(T(24, 8, 30), "Practitioner/synthetic-oncall", "call-1", "Fluids"),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 5, RiskLevel.Medium)], records), null);

        Assert.DoesNotContain(Texts(sbar, SbarPart.Assessment), t => t.StartsWith("Plan", StringComparison.Ordinal));
    }

    [Fact]
    public void NoCallsSaysNoEscalationThisShift()
    {
        var latest = Point(T(24, 7), 1, RiskLevel.Low);
        var sbar = SbarBuilder.Build(Input([latest]), null);

        var sentence = Section(sbar, SbarPart.Assessment).Sentences.Single();
        Assert.Equal("No escalation this shift.", sentence.Text);
        Assert.NotEmpty(sentence.Sources);
    }

    [Fact]
    public void TurkishTemplatesAreUsedForTr()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(24, 0), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Called(T(24, 1), Nurse, "call-2", EscalationTier.SeniorDoctor),
            new Called(T(24, 2), Nurse, "call-3", EscalationTier.CriticalCare),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 3, RiskLevel.Low)], records), "tr");

        Assert.Equal("tr", sbar.Language);
        Assert.Equal("NEWS2 3 Düşük, 07:00.", Texts(sbar, SbarPart.Situation)[0]);
        Assert.Equal("71 yaş, Urinary tract infection.", Texts(sbar, SbarPart.Background)[0]);
        Assert.Equal(["Nöbetçi hekim çağrıldı, 00:00.", "Kıdemli hekim çağrıldı, 01:00.", "Kritik bakım ekibi çağrıldı, 02:00."], Texts(sbar, SbarPart.Assessment));
    }

    [Fact]
    public void SectionsComeInSbarOrder()
    {
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 3, RiskLevel.Low)]), null);

        Assert.Equal([SbarPart.Situation, SbarPart.Background, SbarPart.Assessment, SbarPart.Recommendation], sbar.Sections.Select(s => s.Part));
    }

    [Fact]
    public void EverySentenceHasASource()
    {
        var records = new EscalationRecord[]
        {
            new ConcernRaised(T(23, 20, 15), Nurse, "Restless"),
            new Called(T(23, 23, 19), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Arrived(T(23, 23, 40), "Practitioner/synthetic-oncall", "call-1", false),
            new Called(T(24, 0, 20), Nurse, "call-2", EscalationTier.CriticalCare),
        };
        var points = new[] { Point(T(23, 23, 15), 6, RiskLevel.Medium), Point(T(24, 0, 15), 10, RiskLevel.High), Point(T(24, 7), 4, RiskLevel.Low) };
        var sbar = SbarBuilder.Build(Input(points, records, nextDue: T(24, 8)), null);

        Assert.All(sbar.Sections.SelectMany(s => s.Sentences), s => Assert.NotEmpty(s.Sources));
        Assert.All(sbar.Missing, m => Assert.NotEmpty(m.Sources));
    }

    // ---- Recommendation, contingency (first match wins), missing items, state line ----

    private const string Oncall = "Practitioner/synthetic-oncall";

    private static string Contingency(Sbar sbar) =>
        Texts(sbar, SbarPart.Recommendation).Single(t => t.StartsWith("If", StringComparison.Ordinal) || t.StartsWith("Take", StringComparison.Ordinal)
            || t.StartsWith("Score 7+", StringComparison.Ordinal) || t.StartsWith("Target passed", StringComparison.Ordinal));

    [Fact]
    public void NoObservationsAsksForAFullSet()
    {
        var sbar = SbarBuilder.Build(Input([]), null);

        Assert.Equal(["No observations this shift."], Texts(sbar, SbarPart.Situation));
        Assert.Equal("Take a full set of observations now.", Contingency(sbar));
        Assert.Contains(sbar.Missing, m => m.Kind == MissingKind.NoObservations);
    }

    [Fact]
    public void HighWithoutCriticalCareSaysCallCriticalCareNow()
    {
        var records = new EscalationRecord[] { new Called(T(24, 7, 10), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 8, RiskLevel.High)], records, at: T(24, 7, 20)), null);

        Assert.Equal("Score 7+: call the critical care team now.", Contingency(sbar));
    }

    [Fact]
    public void PassedTargetSaysTellSuggestedTierNow()
    {
        var records = new EscalationRecord[] { new Called(T(24, 0), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var sbar = SbarBuilder.Build(Input([Point(T(23, 23, 50), 5, RiskLevel.Medium)], records, at: T(24, 1, 10)), null);

        Assert.Equal("Target passed at 01:00: tell the senior doctor now.", Contingency(sbar));
    }

    [Fact]
    public void OpenCallWithinTargetNamesTargetAndNextTier()
    {
        var records = new EscalationRecord[] { new Called(T(24, 0), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var sbar = SbarBuilder.Build(Input([Point(T(23, 23, 50), 5, RiskLevel.Medium)], records, at: T(24, 0, 30)), null);

        Assert.Equal("If no review by 01:00, tell the senior doctor.", Contingency(sbar));
        Assert.Contains("On-call doctor called at 00:00, not yet arrived.", Texts(sbar, SbarPart.Recommendation));
    }

    [Fact]
    public void OpenCriticalCareCallSaysChaseTheCall()
    {
        var records = new EscalationRecord[] { new Called(T(24, 0, 20), Nurse, "call-2", EscalationTier.CriticalCare) };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 0, 15), 10, RiskLevel.High)], records, at: T(24, 0, 30)), null);

        Assert.Equal("If the critical care team has not arrived by 00:50, chase the call.", Contingency(sbar));
    }

    [Fact]
    public void HighWithCriticalCareArrivedSaysTellThemIfWorse()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(24, 0, 20), Nurse, "call-2", EscalationTier.CriticalCare),
            new Arrived(T(24, 0, 30), "Practitioner/synthetic-cct", "call-2", false),
        };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 1), 9, RiskLevel.High)], records, at: T(24, 1, 10)), null);

        Assert.Equal("If the score rises further, tell the critical care team.", Contingency(sbar));
    }

    [Fact]
    public void MediumWithoutOpenCallUsesHighTarget()
    {
        var records = new EscalationRecord[]
        {
            new Called(T(24, 6), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Arrived(T(24, 6, 20), Oncall, "call-1", false),
        };
        var options = new EscalationOptions { HighTargetMinutes = 25 };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 5, RiskLevel.Medium)], records, options: options), null);

        Assert.Equal("If the score reaches 7, call a team with critical care skills within 25 min.", Contingency(sbar));
    }

    [Fact]
    public void LowUsesMediumTarget()
    {
        var options = new EscalationOptions { MediumTargetMinutes = 45 };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 2, RiskLevel.Low)], options: options), null);

        Assert.Equal("If the score reaches 5 or one parameter scores 3, ask for a doctor's review within 45 min.", Contingency(sbar));
    }

    [Fact]
    public void NextObservationDueAndOverdue()
    {
        var onTime = SbarBuilder.Build(Input([Point(T(24, 7), 2, RiskLevel.Low)], nextDue: T(24, 8)), null);
        var late = SbarBuilder.Build(Input([Point(T(24, 7), 2, RiskLevel.Low)], nextDue: T(24, 7, 45), overdue: 15), null);

        Assert.Equal("Next observation due 08:00.", Texts(onTime, SbarPart.Recommendation)[0]);
        Assert.Equal("Next observation due 07:45 — overdue by 15 min.", Texts(late, SbarPart.Recommendation)[0]);
        Assert.Contains(late.Missing, m => m.Kind == MissingKind.ObservationOverdue);
        Assert.DoesNotContain(onTime.Missing, m => m.Kind == MissingKind.ObservationOverdue);
    }

    [Fact]
    public void MissingNoCallAfterScoreOnlyFirstOccurrence()
    {
        var sbar = SbarBuilder.Build(Input([Point(T(24, 5), 5, RiskLevel.Medium), Point(T(24, 6), 6, RiskLevel.Medium)]), null);

        var item = sbar.Missing.Single(m => m.Kind == MissingKind.NoCallAfterScore);
        Assert.Equal("No call after NEWS2 5 at 05:00", item.Text);
        Assert.Equal(T(24, 5), item.Sources[0].At);
    }

    [Fact]
    public void CallOpenAtTheScoreCoversIt()
    {
        var records = new EscalationRecord[] { new Called(T(24, 4, 50), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var points = new[] { Point(T(24, 4, 45), 3, RiskLevel.LowMedium), Point(T(24, 5), 5, RiskLevel.Medium) };
        var sbar = SbarBuilder.Build(Input(points, records, at: T(24, 5, 10)), null);

        Assert.DoesNotContain(sbar.Missing, m => m.Kind == MissingKind.NoCallAfterScore);
    }

    [Fact]
    public void AnsweredCallEarlierInWindowCoversLaterScores()
    {
        // Bed 02's night: critical care came at 00:30; a 6 at 01:15 is the same episode, not a missed call.
        var records = new EscalationRecord[]
        {
            new Called(T(24, 0, 20), Nurse, "call-2", EscalationTier.CriticalCare),
            new Arrived(T(24, 0, 30), "Practitioner/synthetic-cct", "call-2", true),
        };
        var points = new[] { Point(T(24, 0, 15), 10, RiskLevel.High), Point(T(24, 1, 15), 6, RiskLevel.Medium) };
        var sbar = SbarBuilder.Build(Input(points, records), null);

        Assert.DoesNotContain(sbar.Missing, m => m.Kind == MissingKind.NoCallAfterScore);
    }

    [Fact]
    public void MissingNoCriticalCareCallAfterSeven()
    {
        var records = new EscalationRecord[] { new Called(T(24, 5), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 4, 55), 5, RiskLevel.Medium), Point(T(24, 6), 7, RiskLevel.High)], records), null);

        Assert.Equal("No critical care call after NEWS2 7 at 06:00", sbar.Missing.Single(m => m.Kind == MissingKind.NoCriticalCareCall).Text);
        Assert.DoesNotContain(sbar.Missing, m => m.Kind == MissingKind.NoCallAfterScore);
    }

    [Fact]
    public void MissingNoArrivalOnlyAfterTarget()
    {
        var records = new EscalationRecord[] { new Called(T(24, 6), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var points = new[] { Point(T(24, 5, 55), 5, RiskLevel.Medium) };

        var before = SbarBuilder.Build(Input(points, records, at: T(24, 6, 50)), null);
        var after = SbarBuilder.Build(Input(points, records, at: T(24, 7, 10)), null);

        Assert.DoesNotContain(before.Missing, m => m.Kind == MissingKind.NoArrival);
        Assert.Equal("No arrival recorded for the on-call doctor call", after.Missing.Single(m => m.Kind == MissingKind.NoArrival).Text);
    }

    [Fact]
    public void MissingAssessmentAndPlan()
    {
        var arrived = new EscalationRecord[]
        {
            new Called(T(24, 6), Nurse, "call-1", EscalationTier.OnCallDoctor),
            new Arrived(T(24, 6, 20), Oncall, "call-1", false),
        };
        var assessed = arrived.Append(new Assessed(T(24, 6, 30), Oncall, "call-1", "Sepsis")).ToArray();
        var points = new[] { Point(T(24, 5, 55), 5, RiskLevel.Medium) };

        Assert.Equal(MissingKind.NoAssessment, SbarBuilder.Build(Input(points, arrived), null).Missing.Single().Kind);
        var plan = SbarBuilder.Build(Input(points, assessed), null).Missing.Single();
        Assert.Equal(MissingKind.NoPlan, plan.Kind);
        Assert.Equal("No plan recorded for the on-call doctor call", plan.Text);
        Assert.Equal("Task/call-1", plan.Sources.Single().Reference);
    }

    [Fact]
    public void StateLineSummarisesTheChain()
    {
        var waiting = new EscalationRecord[] { new Called(T(24, 0), Nurse, "call-1", EscalationTier.OnCallDoctor) };
        var arrived = new EscalationRecord[]
        {
            new Called(T(24, 1), Nurse, "call-2", EscalationTier.CriticalCare),
            new Arrived(T(24, 1, 30), "Practitioner/synthetic-cct", "call-2", false),
        };
        var points = new[] { Point(T(23, 23, 50), 5, RiskLevel.Medium) };

        Assert.Equal("On-call doctor called 00:00, waiting 41 min", SbarBuilder.Build(Input(points, waiting, at: T(24, 0, 41)), null).StateLine);
        Assert.Equal("Critical care team arrived 01:30", SbarBuilder.Build(Input(points, arrived), null).StateLine);
        Assert.Equal("Stable", SbarBuilder.Build(Input([Point(T(24, 7), 1, RiskLevel.Low)]), null).StateLine);
        Assert.Equal("Not escalated", SbarBuilder.Build(Input([Point(T(24, 7), 5, RiskLevel.Medium)]), null).StateLine);
        Assert.Equal("Stabil", SbarBuilder.Build(Input([Point(T(24, 7), 1, RiskLevel.Low)]), "tr").StateLine);
    }

    [Fact]
    public void ConcernInWindowIsAnOpenItem()
    {
        var records = new EscalationRecord[] { new ConcernRaised(T(23, 20, 15), Nurse, "Restless") };
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 1, RiskLevel.Low)], records), null);

        Assert.Contains("Concern from 20:15 still open.", Texts(sbar, SbarPart.Recommendation));
    }

    [Fact]
    public void TurkishContingency()
    {
        var sbar = SbarBuilder.Build(Input([Point(T(24, 7), 2, RiskLevel.Low)], nextDue: T(24, 8)), "tr");

        Assert.Equal(
            ["Sonraki ölçüm 08:00.", "Puan 5'e ulaşırsa ya da tek bir bulgu 3 puan alırsa 60 dk içinde hekim değerlendirmesi isteyin."],
            Texts(sbar, SbarPart.Recommendation));
    }
}
