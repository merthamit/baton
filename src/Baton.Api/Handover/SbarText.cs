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

    // Source labels
    public string ObservationsLabel(string time) => tr ? $"Ölçüm {time}" : $"Observations {time}";

    public string ConcernLabel(string time) => tr ? $"Hemşire endişesi {time}" : $"Nurse concern {time}";

    public string FamilyLabel(string time) => tr ? $"Hasta/yakın endişesi {time}" : $"Patient/family concern {time}";

    public string CallLabel(EscalationTier tier, string time) => tr ? $"{Tier(tier)} çağrısı {time}" : $"{Tier(tier)} call {time}";

    public string PatientLabel => tr ? "Hasta kaydı" : "Patient record";

    public string DiagnosisLabel => tr ? "Tanı" : "Diagnosis";
}
