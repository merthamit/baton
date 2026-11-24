using Hl7.Fhir.Model;

namespace Baton.Fhir;

/// <summary>
/// The standard FHIR translation extension (<c>http://hl7.org/fhir/StructureDefinition/translation</c>, sub-extensions
/// <c>lang</c> and <c>content</c>) on string and markdown elements. The element keeps its original text; translations sit
/// beside it, one per language, and a language-aware client picks the one it needs.
/// </summary>
public static class FhirTranslation
{
    public const string Url = "http://hl7.org/fhir/StructureDefinition/translation";

    public static void Add(Element element, string language, string content)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(language);
        ArgumentNullException.ThrowIfNull(content);

        element.Extension.Add(new Extension
        {
            Url = Url,
            Extension =
            [
                new Extension("lang", new Code(language)),
                new Extension("content", element is Markdown ? new Markdown(content) : new FhirString(content)),
            ],
        });
    }

    /// <summary>Every translation on the element, by language.</summary>
    public static IReadOnlyDictionary<string, string> All(Element? element)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in element?.Extension.Where(e => e.Url == Url) ?? [])
        {
            if ((e.GetExtension("lang")?.Value as Code)?.Value is { } lang && Get(element, lang) is { } text)
            {
                result.TryAdd(lang, text);
            }
        }

        return result;
    }

    /// <summary>The translation for <paramref name="language"/>, or null when there is none.</summary>
    public static string? Get(Element? element, string? language)
    {
        if (element is null || string.IsNullOrEmpty(language))
        {
            return null;
        }

        return element.Extension
            .Where(e => e.Url == Url)
            .Where(e => (e.GetExtension("lang")?.Value as Code)?.Value == language)
            .Select(e => e.GetExtension("content")?.Value switch
            {
                Markdown m => m.Value,
                FhirString s => s.Value,
                _ => null,
            })
            .FirstOrDefault(v => v is not null);
    }
}
