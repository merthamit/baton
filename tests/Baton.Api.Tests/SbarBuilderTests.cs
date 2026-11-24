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
}
