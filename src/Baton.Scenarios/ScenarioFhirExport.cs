using System.Globalization;
using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Scenarios;

/// <summary>Writes one simulated measurement as seven LOINC-coded FHIR R4 vital-sign Observations.</summary>
public static class ScenarioFhirExport
{
    private const string Loinc = "http://loinc.org";
    private const string Ucum = "http://unitsofmeasure.org";
    private const string CategorySystem = "http://terminology.hl7.org/CodeSystem/observation-category";

    private static readonly Dictionary<Consciousness, (string Code, string Display)> Acvpu = new()
    {
        [Consciousness.Alert] = ("LA9340-6", "Alert"),
        [Consciousness.Confusion] = ("LA6560-2", "Confused"),
        [Consciousness.Voice] = ("LA17108-4", "Responds to voice"),
        [Consciousness.Pain] = ("LA17107-6", "Responds to pain"),
        [Consciousness.Unresponsive] = ("LA9343-0", "Unresponsive"),
    };

    public static IReadOnlyList<Observation> ToObservations(WardBed bed, Measurement measurement)
    {
        ArgumentNullException.ThrowIfNull(bed);
        ArgumentNullException.ThrowIfNull(measurement);
        return ToObservations(bed.PatientId, measurement.At, measurement.Vitals, string.Create(CultureInfo.InvariantCulture, $"{measurement.Hour * 60:0000}"));
    }

    /// <summary>One set of vitals for a patient at a moment; ids are <c>{patientId}-{idStamp}-{suffix}</c>.</summary>
    public static IReadOnlyList<Observation> ToObservations(string patientId, DateTimeOffset at, VitalSet vitals, string idStamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(patientId);
        ArgumentNullException.ThrowIfNull(vitals);
        ArgumentException.ThrowIfNullOrEmpty(idStamp);

        var v = vitals;
        Observation Make(string suffix, string code, string display, DataType? value) => new()
        {
            Id = $"{patientId}-{idStamp}-{suffix}",
            Status = ObservationStatus.Final,
            Category = [new CodeableConcept { Coding = [new Coding(CategorySystem, "vital-signs", "Vital Signs")] }],
            Code = new CodeableConcept { Coding = [new Coding(Loinc, code, display)], Text = display },
            Subject = new ResourceReference($"Patient/{patientId}"),
            Effective = new FhirDateTime(at),
            Value = value,
        };

        Quantity Q(decimal value, string unit) => new() { Value = value, System = Ucum, Code = unit, Unit = unit };

        // FHIR R4 vital-signs profiles: SpO2 carries 2708-6 (and the pulse-oximetry code), blood pressure is the
        // 85354-9 panel with systolic and diastolic components; the scenario has no diastolic value, so it is absent.
        var spo2 = Make("spo2", "2708-6", "Oxygen saturation in Arterial blood", Q(v.SpO2, "%"));
        spo2.Code.Coding.Add(new Coding(Loinc, "59408-5", "Oxygen saturation in Arterial blood by Pulse oximetry"));
        var bp = Make("sbp", "85354-9", "Blood pressure panel with all children optional", null);
        bp.Component =
        [
            new Observation.ComponentComponent { Code = new CodeableConcept(Loinc, "8480-6", "Systolic blood pressure"), Value = Q(v.SystolicBp, "mm[Hg]") },
            new Observation.ComponentComponent
            {
                Code = new CodeableConcept(Loinc, "8462-4", "Diastolic blood pressure"),
                DataAbsentReason = new CodeableConcept("http://terminology.hl7.org/CodeSystem/data-absent-reason", "not-performed", "Not Performed"),
            },
        ];

        var (acvpuCode, acvpuDisplay) = Acvpu[v.Consciousness];
        return
        [
            Make("rr", "9279-1", "Respiratory rate", Q(v.RespirationRate, "/min")),
            spo2,
            Make("o2", "3151-8", "Inhaled oxygen flow rate", Q(v.OxygenLitresPerMinute, "L/min")),
            bp,
            Make("hr", "8867-4", "Heart rate", Q(v.PulseRate, "/min")),
            Make("temp", "8310-5", "Body temperature", Q(v.TemperatureCelsius, "Cel")),
            Make("acvpu", "67775-7", "Level of responsiveness", new CodeableConcept { Coding = [new Coding(Loinc, acvpuCode, acvpuDisplay)], Text = acvpuDisplay }),
        ];
    }
}
