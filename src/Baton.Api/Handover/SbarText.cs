using System.Globalization;
using Baton.Core.Escalation;
using Baton.Core.News2;

namespace Baton.Api.Handover;

/// <summary>
/// The SBAR sentence templates in English and Turkish. Only the template is translated; free text from the record
/// (concern notes, assessment, plan) arrives already in the requested language when a translation exists.
/// </summary>
internal sealed class SbarText
{
    public static readonly SbarText English = new(tr: false);
    public static readonly SbarText Turkish = new(tr: true);

    private readonly bool tr;

    private SbarText(bool tr) => this.tr = tr;

    public static SbarText For(string? language) => language == "tr" ? Turkish : English;

    public string Language => tr ? "tr" : "en";

    public CultureInfo Culture => tr ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.InvariantCulture;

    public string Risk(RiskLevel? risk) => (risk, tr) switch
    {
        (RiskLevel.Low, false) => "Low",
        (RiskLevel.LowMedium, false) => "Low–medium",
        (RiskLevel.Medium, false) => "Medium",
        (RiskLevel.High, false) => "High",
        (RiskLevel.Low, true) => "Düşük",
        (RiskLevel.LowMedium, true) => "Düşük–orta",
        (RiskLevel.Medium, true) => "Orta",
        (RiskLevel.High, true) => "Yüksek",
        (_, false) => "unknown risk",
        (_, true) => "risk bilinmiyor",
    };

    public string Tier(EscalationTier tier) => (tier, tr) switch
    {
        (EscalationTier.OnCallDoctor, false) => "On-call doctor",
        (EscalationTier.SeniorDoctor, false) => "Senior doctor",
        (EscalationTier.CriticalCare, false) => "Critical care team",
        (EscalationTier.OnCallDoctor, true) => "Nöbetçi hekim",
        (EscalationTier.SeniorDoctor, true) => "Kıdemli hekim",
        _ => "Kritik bakım ekibi",
    };

    /// <summary>The tier inside a sentence ("tell the senior doctor"): lower case in English.</summary>
    public string TierInSentence(EscalationTier tier) => tr ? Tier(tier).ToLower(Culture) : Tier(tier).ToLowerInvariant();

    public string Parameter(News2Parameter parameter) => (parameter, tr) switch
    {
        (News2Parameter.RespirationRate, false) => "Resp",
        (News2Parameter.SpO2, _) => "SpO₂",
        (News2Parameter.SupplementalOxygen, false) => "Oxygen",
        (News2Parameter.SystolicBp, false) => "Systolic",
        (News2Parameter.PulseRate, false) => "Pulse",
        (News2Parameter.Consciousness, false) => "ACVPU",
        (News2Parameter.Temperature, false) => "Temp",
        (News2Parameter.RespirationRate, true) => "Solunum",
        (News2Parameter.SupplementalOxygen, true) => "Oksijen",
        (News2Parameter.SystolicBp, true) => "Sistolik",
        (News2Parameter.PulseRate, true) => "Nabız",
        (News2Parameter.Consciousness, true) => "Bilinç",
        _ => "Ateş",
    };

    // Situation
    public string Score(int total, RiskLevel? risk, string time) => tr ? $"NEWS2 {total} {Risk(risk)}, {time}" : $"NEWS2 {total} {Risk(risk)} at {time}";

    public string NotScored(string time) => tr ? $"NEWS2 hesaplanamadı, {time}" : $"NEWS2 not scored at {time}";

    public string Incomplete(string time, string parameters) => tr ? $"{time} ölçümü eksik: {parameters}" : $"Latest set incomplete at {time}: {parameters}";

    public string Peak(int total, RiskLevel? risk, string time) => tr ? $"Vardiyanın en yükseği: {total} {Risk(risk)}, {time}" : $"Highest this shift: {total} {Risk(risk)} at {time}";

    public string Drivers(string list) => tr ? $"Puanı yükseltenler: {list}" : $"Driven by: {list}";

    public string NoObservations => tr ? "Bu vardiyada ölçüm yok" : "No observations this shift";

    // Background
    public string AgeAndDiagnosis(int? age, string? diagnosis) => (age, diagnosis, tr) switch
    {
        (null, null, false) => "No age or diagnosis recorded",
        (null, null, true) => "Yaş ve tanı kayıtlı değil",
        ({ } a, null, false) => $"{a}",
        ({ } a, null, true) => $"{a} yaş",
        (null, { } d, _) => d,
        ({ } a, { } d, false) => $"{a}, {d}",
        ({ } a, { } d, true) => $"{a} yaş, {d}",
    };

    public string NurseConcern(string time, string note) => tr ? $"Hemşire endişesi, {time}: {note}" : $"Nurse concern at {time}: {note}";

    public string FamilyConcern(string time, string text) => tr ? $"Hasta/yakın endişesi, {time}: {text}" : $"Patient/family concern at {time}: {text}";

    // Assessment
    public string Called(EscalationTier tier, string time) => tr ? $"{Tier(tier)} çağrıldı, {time}" : $"{Tier(tier)} called at {time}";

    public string Arrived(string time, bool simulated) => (tr, simulated) switch
    {
        (false, false) => $"Arrived at {time}",
        (false, true) => $"Arrived at {time} (simulated)",
        (true, false) => $"Geldi, {time}",
        (true, true) => $"Geldi, {time} (simülasyon)",
    };

    public string Assessment(string text) => tr ? $"Değerlendirme: {text}" : $"Assessment: {text}";

    public string Plan(string text) => $"Plan: {text}";

    public string NoEscalation => tr ? "Bu vardiyada çağrı yok" : "No escalation this shift";

    // Recommendation
    public string NextDue(string time, int? overdueMinutes) => (tr, overdueMinutes is > 0) switch
    {
        (false, false) => $"Next observation due {time}",
        (false, true) => $"Next observation due {time} — overdue by {overdueMinutes} min",
        (true, false) => $"Sonraki ölçüm {time}",
        (true, true) => $"Sonraki ölçüm {time} — {overdueMinutes} dk gecikti",
    };

    public string CompleteSet(string? parameters) => (tr, parameters) switch
    {
        (false, null) => "Complete the observation set now",
        (false, { } p) => $"Complete the observation set now: {p}",
        (true, null) => "Ölçüm setini hemen tamamlayın",
        (true, { } p) => $"Ölçüm setini hemen tamamlayın: {p}",
    };

    public string TakeObservations => tr ? "Hemen tam bir ölçüm seti alın" : "Take a full set of observations now";

    public string CallCriticalCareNow => tr ? "Puan 7 ve üstü: kritik bakım ekibini hemen çağırın" : "Score 7+: call the critical care team now";

    public string TargetPassed(string target, EscalationTier tier) =>
        tr ? $"Hedef {target} aşıldı: şimdi {TierInSentence(tier)} ile görüşün" : $"Target passed at {target}: tell the {TierInSentence(tier)} now";

    public string IfNoReview(string? target, EscalationTier next) => (tr, target) switch
    {
        (false, null) => $"If no review comes, tell the {TierInSentence(next)}",
        (false, { } at) => $"If no review by {at}, tell the {TierInSentence(next)}",
        (true, null) => $"Değerlendirme gelmezse {TierInSentence(next)} ile görüşün",
        (true, { } at) => $"{at} saatine kadar değerlendirme olmazsa {TierInSentence(next)} ile görüşün",
    };

    public string ChaseCriticalCare(string? target) => (tr, target) switch
    {
        (false, null) => "If the critical care team does not arrive, chase the call",
        (false, { } at) => $"If the critical care team has not arrived by {at}, chase the call",
        (true, null) => "Kritik bakım ekibi gelmezse çağrıyı yineleyin",
        (true, { } at) => $"Kritik bakım ekibi {at} saatine kadar gelmezse çağrıyı yineleyin",
    };

    public string TellCriticalCareIfWorse => tr ? "Puan daha da yükselirse kritik bakım ekibi ile görüşün" : "If the score rises further, tell the critical care team";

    public string IfReachesSeven(int minutes) =>
        tr ? $"Puan 7'ye ulaşırsa {minutes} dk içinde kritik bakım becerili bir ekip çağırın" : $"If the score reaches 7, call a team with critical care skills within {minutes} min";

    public string IfReachesFive(int minutes) =>
        tr ? $"Puan 5'e ulaşırsa ya da tek bir bulgu 3 puan alırsa {minutes} dk içinde hekim değerlendirmesi isteyin"
           : $"If the score reaches 5 or one parameter scores 3, ask for a doctor's review within {minutes} min";

    public string OpenCall(EscalationTier tier, string time) => tr ? $"{Tier(tier)} çağrıldı, {time}; henüz gelmedi" : $"{Tier(tier)} called at {time}, not yet arrived";

    public string OpenConcern(string time) => tr ? $"{time} endişesi açık" : $"Concern from {time} still open";

    public string OpenFamilyConcern(string time) => tr ? $"{time} hasta/yakın endişesi açık" : $"Patient/family concern from {time} still open";

    // Missing items (tags: no full stop)
    public string MissingNoObservations => NoObservations;

    public string MissingNoCall(int total, string time) => tr ? $"NEWS2 {total} ({time}) sonrası çağrı yok" : $"No call after NEWS2 {total} at {time}";

    public string MissingNoCriticalCare(int total, string time) =>
        tr ? $"NEWS2 {total} ({time}) sonrası kritik bakım çağrısı yok" : $"No critical care call after NEWS2 {total} at {time}";

    public string MissingArrival(EscalationTier tier) => tr ? $"{Tier(tier)} çağrısı için geliş kaydı yok" : $"No arrival recorded for the {TierInSentence(tier)} call";

    public string MissingAssessment(EscalationTier tier) =>
        tr ? $"{Tier(tier)} çağrısı için değerlendirme kaydı yok" : $"No assessment recorded for the {TierInSentence(tier)} call";

    public string MissingPlan(EscalationTier tier) => tr ? $"{Tier(tier)} çağrısı için plan kaydı yok" : $"No plan recorded for the {TierInSentence(tier)} call";

    public string MissingOverdue(int minutes) => tr ? $"Ölçüm {minutes} dk gecikti" : $"Observation overdue by {minutes} min";

    // State line (list row)
    public string StatePlanned(EscalationTier tier, string time) => tr ? $"{Tier(tier)}: plan kaydedildi {time}" : $"{Tier(tier)}: plan recorded {time}";

    public string StateAssessed(EscalationTier tier, string time) => tr ? $"{Tier(tier)} değerlendirdi {time}" : $"{Tier(tier)} assessed {time}";

    public string StateArrived(EscalationTier tier, string time) => tr ? $"{Tier(tier)} geldi {time}" : $"{Tier(tier)} arrived {time}";

    public string StateWaiting(EscalationTier tier, string time, int minutes) =>
        tr ? $"{Tier(tier)} çağrıldı {time}, {minutes} dk bekliyor" : $"{Tier(tier)} called {time}, waiting {minutes} min";

    public string StateStable => tr ? "Stabil" : "Stable";

    public string StateIncomplete => tr ? "Ölçüm seti eksik" : "Set incomplete";

    public string StateNotEscalated => tr ? "Çağrı yapılmadı" : "Not escalated";

    public string StateNoObservations => tr ? "Ölçüm yok" : "No observations";

    // Source labels
    public string ObservationsLabel(string time) => tr ? $"Ölçüm {time}" : $"Observations {time}";

    public string ConcernLabel(string time) => tr ? $"Hemşire endişesi {time}" : $"Nurse concern {time}";

    public string FamilyLabel(string time) => tr ? $"Hasta/yakın endişesi {time}" : $"Patient/family concern {time}";

    public string CallLabel(EscalationTier tier, string time) => tr ? $"{Tier(tier)} çağrısı {time}" : $"{Tier(tier)} call {time}";

    public string PatientLabel => tr ? "Hasta kaydı" : "Patient record";

    public string DiagnosisLabel => tr ? "Tanı" : "Diagnosis";
}
