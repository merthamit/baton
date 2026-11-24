using System.Globalization;
using System.Text.RegularExpressions;

namespace Baton.Scenarios;

/// <summary>
/// Turkish versions of every text the synthetic scenarios produce (notes, plans, diagnoses, titles and the generator's
/// own messages). They travel in FHIR with the standard translation extension, so a language-aware client can show the
/// data in Turkish. Clinical wording reviewed by the project owner; free text a user types is never translated.
/// </summary>
public static partial class ScenarioTranslations
{
    private static readonly Dictionary<string, string> Fixed = new(StringComparer.Ordinal)
    {
        // Diagnoses
        ["Acute pyelonephritis, antibiotics day 2"] = "Akut piyelonefrit, antibiyotik 2. gün",
        ["COPD, hypercapnic respiratory failure"] = "KOAH, hiperkapnik solunum yetmezliği",
        ["Community-acquired pneumonia"] = "Toplum kökenli pnömoni",
        ["Day 1 after laparoscopic cholecystectomy"] = "Laparoskopik kolesistektomi sonrası 1. gün",
        ["Upper GI bleed under observation"] = "Üst GİS kanama gözlemi",
        ["Lower-leg cellulitis"] = "Alt ekstremite selüliti",
        ["Urinary tract infection, catheterised"] = "İdrar yolu enfeksiyonu, kateterli",
        ["Diabetic foot ulcer"] = "Diyabetik ayak yarası",

        // Scenario titles
        ["Stable patient"] = "Stabil hasta",
        ["Quiet sepsis"] = "Sessiz sepsis",
        ["Hidden bleed"] = "Gizli kanama",
        ["COPD exacerbation (Scale 2)"] = "KOAH alevlenmesi (Skala 2)",
        ["Recovering patient"] = "İyileşen hasta",

        // Notes, assessments and plans on the cards
        ["Restless, not eating, not herself"] = "Huzursuz, yemek yemiyor, kendinde değil",
        ["On-call doctor: suspected sepsis"] = "Nöbetçi hekim: sepsis şüphesi",
        ["Blood cultures, lactate, IV fluids, oxygen 2 L/min, antibiotics"] = "Kan kültürleri, laktat, IV sıvı, oksijen 2 L/dk, antibiyotik",
        ["Pale, clammy, melaena"] = "Soluk, terli, melena",
        ["Blatchford score, two cannulas, group and save, fluids"] = "Blatchford skoru, iki damar yolu, kan grubu ve antikor taraması, sıvı",
        ["Transfusion by haemoglobin; urgent endoscopy after resuscitation"] = "Hemoglobine göre transfüzyon; resüsitasyon sonrası acil endoskopi",
        ["Scale 2, target 88-92%: hypercapnia confirmed on previous admission blood gas"] = "Skala 2, hedef %88–92: önceki yatıştaki kan gazında hiperkapni doğrulanmış",
        ["More breathless, pausing between sentences"] = "Nefes darlığı arttı, cümle arasında duruyor",
        ["Oxygen turned up to 4 L/min; Scale 2 scores the high saturation"] = "Oksijen 4 L/dk'ya çıkarıldı; Skala 2 yüksek satürasyonu puanlıyor",
        ["Doctor: nebulisers and steroid; do not increase oxygen"] = "Hekim: nebül ve steroid; oksijeni artırmayın",
        ["Nebulised bronchodilator and steroid"] = "Nebül bronkodilatör ve steroid",
        ["Blood gas requested; oxygen back to 2 L/min"] = "Kan gazı istendi; oksijen yeniden 2 L/dk",
        ["Antibiotics day 2; fever usually settles within 1.5-3 days"] = "Antibiyotik 2. gün; ateş genellikle 1,5–3 günde düşer",
        ["Ward doctor: responding to antibiotics; continue, paracetamol, recheck hourly"] = "Servis hekimi: antibiyotiğe yanıt var; devam, parasetamol, saatlik kontrol",

        // Generator messages
        ["Nurse called the on-call doctor"] = "Hemşire nöbetçi hekimi çağırdı",
        ["the team was at bed 02's emergency"] = "ekip yatak 02'nin acil durumundaydı",
    };

    [GeneratedRegex(@"^NEWS2 (\d+): urgent review needed$")]
    private static partial Regex UrgentReview();

    [GeneratedRegex(@"^NEWS2 (\d+): critical care team called$")]
    private static partial Regex CriticalCareCalled();

    [GeneratedRegex(@"^Observations due at (\d{2}:\d{2}) taken (\d+) min late: (.+)$")]
    private static partial Regex LateObservation();

    /// <summary>The Turkish version of a scenario text, or null when the text is not one the scenarios produce.</summary>
    public static string? ToTurkish(string english)
    {
        ArgumentNullException.ThrowIfNull(english);
        if (Fixed.TryGetValue(english, out var turkish))
        {
            return turkish;
        }

        if (UrgentReview().Match(english) is { Success: true } urgent)
        {
            return string.Create(CultureInfo.InvariantCulture, $"NEWS2 {urgent.Groups[1].Value}: acil değerlendirme gerekli");
        }

        if (CriticalCareCalled().Match(english) is { Success: true } critical)
        {
            return string.Create(CultureInfo.InvariantCulture, $"NEWS2 {critical.Groups[1].Value}: kritik bakım ekibi çağrıldı");
        }

        if (LateObservation().Match(english) is { Success: true } late)
        {
            var reason = ToTurkish(late.Groups[3].Value) ?? late.Groups[3].Value;
            return string.Create(CultureInfo.InvariantCulture, $"{late.Groups[1].Value} ölçümü {late.Groups[2].Value} dk geç alındı: {reason}");
        }

        return null;
    }
}
