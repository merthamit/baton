namespace Baton.Scenarios.Tests;

// Every text the scenarios produce has a Turkish version, so the board can show the data in the chosen language.
public class ScenarioTranslationsTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;

    public static TheoryData<int> Delays => new() { 0, 3 };

    [Theory]
    [MemberData(nameof(Delays))]
    public void EveryWardText_HasATurkishVersion(int delay)
    {
        var ward = Ward.Generate(Start, 42, treatmentDelayHours: delay, beds: Ward.MaxBeds);
        var texts = ward.SelectMany(b => b.Patient.Events.Select(e => e.Text))
            .Concat(ward.Select(b => b.Diagnosis))
            .Concat(ward.Select(b => b.Patient.Card.Title))
            .Distinct()
            .ToList();

        var missing = texts.Where(t => ScenarioTranslations.ToTurkish(t) is null).ToList();

        Assert.True(missing.Count == 0, "No Turkish for: " + string.Join(" | ", missing));
    }

    [Theory]
    [InlineData("Restless, not eating, not herself", "Huzursuz, yemek yemiyor, kendinde değil")]
    [InlineData("Community-acquired pneumonia", "Toplum kökenli pnömoni")]
    [InlineData("NEWS2 6: urgent review needed", "NEWS2 6: acil değerlendirme gerekli")]
    [InlineData("NEWS2 10: critical care team called", "NEWS2 10: kritik bakım ekibi çağrıldı")]
    [InlineData("Observations due at 00:30 taken 45 min late: the team was at bed 02's emergency", "00:30 ölçümü 45 dk geç alındı: ekip yatak 02'nin acil durumundaydı")]
    public void KnownTexts_Translate(string english, string turkish)
    {
        Assert.Equal(turkish, ScenarioTranslations.ToTurkish(english));
    }

    [Fact]
    public void UnknownText_HasNoTranslation()
    {
        Assert.Null(ScenarioTranslations.ToTurkish("Something a nurse typed in Try mode"));
    }
}
