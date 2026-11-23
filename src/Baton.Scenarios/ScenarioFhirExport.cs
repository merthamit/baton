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

        var v = measurement.Vitals;
        var stamp = string.Create(CultureInfo.InvariantCulture, $"{measurement.Hour * 60:0000}");
        Observation Make(string suffix, string code, string display, DataType value) => new()
        {
            Id = $"{bed.PatientId}-{stamp}-{suffix}",
            Status = ObservationStatus.Final,
            Category = [new CodeableConcept { Coding = [new Coding(CategorySystem, "vital-signs", "Vital Signs")] }],
            Code = new CodeableConcept { Coding = [new Coding(Loinc, code, display)], Text = display },
            Subject = new ResourceReference($"Patient/{bed.PatientId}"),
            Effective = new FhirDateTime(measurement.At),
            Value = value,
        };

        Quantity Q(decimal value, string unit) => new() { Value = value, System = Ucum, Code = unit, Unit = unit };

        var (acvpuCode, acvpuDisplay) = Acvpu[v.Consciousness];
        return
        [
            Make("rr", "9279-1", "Respiratory rate", Q(v.RespirationRate, "/min")),
            Make("spo2", "59408-5", "Oxygen saturation in Arterial blood by Pulse oximetry", Q(v.SpO2, "%")),
            Make("o2", "3151-8", "Inhaled oxygen flow rate", Q(v.OxygenLitresPerMinute, "L/min")),
            Make("sbp", "8480-6", "Systolic blood pressure", Q(v.SystolicBp, "mm[Hg]")),
            Make("hr", "8867-4", "Heart rate", Q(v.PulseRate, "/min")),
            Make("temp", "8310-5", "Body temperature", Q(v.TemperatureCelsius, "Cel")),
            Make("acvpu", "67775-7", "Level of responsiveness", new CodeableConcept { Coding = [new Coding(Loinc, acvpuCode, acvpuDisplay)], Text = acvpuDisplay }),
        ];
    }
}
