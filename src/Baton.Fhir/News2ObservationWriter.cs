using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir;

/// <summary>
/// Writes a complete NEWS2 result as one FHIR R4 Observation: the total in <c>valueQuantity</c>, the seven
/// sub-scores in <c>component</c> (SNOMED CT sub-score codes) and the source vital signs in
/// <c>derivedFrom</c>, so every point can be traced back to the reading it came from.
/// </summary>
public static class News2ObservationWriter
{
    private const string UcumSystem = "http://unitsofmeasure.org";
    private const string CategorySystem = "http://terminology.hl7.org/CodeSystem/observation-category";

    public static Observation Create(News2Result result, VitalSignsReading reading, ResourceReference subject, DateTimeOffset effective)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(subject);

        if (!result.IsComplete || result.Total is not { } total)
        {
            throw new ArgumentException("Only a complete NEWS2 result can be written as an Observation.", nameof(result));
        }

        var derivedFrom = new List<ResourceReference>();
        foreach (var source in reading.Sources.Values.Distinct())
        {
            if (string.IsNullOrEmpty(source.Id))
            {
                throw new ArgumentException("Every source Observation needs an id so it can be referenced in derivedFrom.", nameof(reading));
            }

            // Never attach one patient's vital signs to another patient's score.
            if (source.Subject?.Reference is not { } sourceSubject || sourceSubject != subject.Reference)
            {
                throw new ArgumentException(
                    $"Source Observation/{source.Id} belongs to '{source.Subject?.Reference ?? "no subject"}', not '{subject.Reference}'.",
                    nameof(reading));
            }

            var reference = $"Observation/{source.Id}";
            if (derivedFrom.All(r => r.Reference != reference))
            {
                derivedFrom.Add(new ResourceReference(reference));
            }
        }

        return new Observation
        {
            Status = ObservationStatus.Final,
            Category = [Concept(CategorySystem, "survey", "Survey")],
            Code = Concept(Snomed.System, Snomed.News2Total, Snomed.News2TotalDisplay),
            Subject = subject,
            Effective = new FhirDateTime(effective),
            Value = Score(total),
            Method = new CodeableConcept { Text = result.AlgorithmVersion },
            Note = [new Annotation { Text = new Markdown("Calculated by Baton from synthetic data. Not a medical device.") }],
            DerivedFrom = derivedFrom,
            Component = result.SubScores
                .Select(kv => (Parameter: kv.Key, Points: kv.Value!.Value))
                .Select(s =>
                {
                    var (code, display) = Snomed.SubScore(s.Parameter, reading.Input.SpO2Scale);
                    return new Observation.ComponentComponent
                    {
                        Code = Concept(Snomed.System, code, display),
                        Value = Score(s.Points),
                    };
                })
                .ToList(),
        };
    }

    // CodeableConcept(system, code, text) sets text, not the coding's display, so build the coding explicitly.
    private static CodeableConcept Concept(string system, string code, string display) =>
        new() { Coding = [new Coding(system, code, display)], Text = display };

    private static Quantity Score(int points) => new() { Value = points, System = UcumSystem, Code = "{score}", Unit = "score" };
}
