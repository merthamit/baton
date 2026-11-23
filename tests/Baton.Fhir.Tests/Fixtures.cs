using Hl7.Fhir.Model;

namespace Baton.Fhir.Tests;

/// <summary>Synthetic FHIR R4 vital-sign Observations for tests.</summary>
internal static class Fixtures
{
    public const string LoincSystem = "http://loinc.org";
    public const string UcumSystem = "http://unitsofmeasure.org";

    public static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    public static ReadOptions Options(TimeSpan? maxAge = null) => new(Now, maxAge ?? TimeSpan.FromHours(4));

    public const string PatientReference = "Patient/synthetic-1";

    public static Observation Vital(string id, string loinc, decimal value, string ucum, DateTimeOffset at) => new()
    {
        Id = id,
        Status = ObservationStatus.Final,
        Subject = new ResourceReference(PatientReference),
        Code = new CodeableConcept(LoincSystem, loinc),
        Effective = new FhirDateTime(at),
        Value = new Quantity { Value = value, System = UcumSystem, Code = ucum, Unit = ucum },
    };

    public static Observation Consciousness(string id, string answer, DateTimeOffset at, string loinc = "67775-7") => new()
    {
        Id = id,
        Status = ObservationStatus.Final,
        Subject = new ResourceReference(PatientReference),
        Code = new CodeableConcept(LoincSystem, loinc),
        Effective = new FhirDateTime(at),
        Value = new CodeableConcept(LoincSystem, answer),
    };

    public static Observation BloodPressurePanel(string id, decimal systolic, decimal diastolic, DateTimeOffset at) => new()
    {
        Id = id,
        Status = ObservationStatus.Final,
        Subject = new ResourceReference(PatientReference),
        Code = new CodeableConcept(LoincSystem, "85354-9"),
        Effective = new FhirDateTime(at),
        Component =
        [
            new Observation.ComponentComponent
            {
                Code = new CodeableConcept(LoincSystem, "8480-6"),
                Value = new Quantity { Value = systolic, System = UcumSystem, Code = "mm[Hg]", Unit = "mm[Hg]" },
            },
            new Observation.ComponentComponent
            {
                Code = new CodeableConcept(LoincSystem, "8462-4"),
                Value = new Quantity { Value = diastolic, System = UcumSystem, Code = "mm[Hg]", Unit = "mm[Hg]" },
            },
        ],
    };

    /// <summary>All seven parameters, every one scoring 0, recorded ten minutes before <see cref="Now"/>.</summary>
    public static List<Observation> NormalSet(DateTimeOffset? at = null)
    {
        var t = at ?? Now.AddMinutes(-10);
        return
        [
            Vital("rr", "9279-1", 16, "/min", t),
            Vital("spo2", "59408-5", 97, "%", t),
            Vital("o2flow", "3151-8", 0, "L/min", t),
            Vital("sbp", "8480-6", 120, "mm[Hg]", t),
            Vital("hr", "8867-4", 70, "/min", t),
            Consciousness("acvpu", "LA9340-6", t),
            Vital("temp", "8310-5", 37.0m, "Cel", t),
        ];
    }
}
