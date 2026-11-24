#nullable disable warnings
using Hl7.Fhir.Model;

namespace Baton.Scenarios.Tests;

// The official HL7 validator applies the FHIR R4 vital-signs profiles by LOINC code: blood pressure must be the
// 85354-9 panel with systolic and diastolic components (no top-level value), and oxygen saturation must carry 2708-6.
public class VitalSignsProfileTests
{
    private static readonly WardBed Bed = Ward.Generate(CardFidelityTests.Quiet.ShiftStart, 42)[1];

    private static IReadOnlyList<Observation> First() => ScenarioFhirExport.ToObservations(Bed, Bed.Patient.Measurements[0]);

    [Fact]
    public void BloodPressureIsThePanelWithBothComponents()
    {
        var bp = First().Single(o => o.Id!.EndsWith("-sbp", StringComparison.Ordinal));

        Assert.Equal("85354-9", bp.Code.Coding.Single().Code);
        Assert.Null(bp.Value);
        var systolic = bp.Component.Single(c => c.Code.Coding.Any(x => x.Code == "8480-6"));
        Assert.Equal(Bed.Patient.Measurements[0].Vitals.SystolicBp, ((Quantity)systolic.Value).Value);
        var diastolic = bp.Component.Single(c => c.Code.Coding.Any(x => x.Code == "8462-4"));
        Assert.Null(diastolic.Value);
        Assert.Equal("not-performed", diastolic.DataAbsentReason.Coding.Single().Code);
    }

    [Fact]
    public void OxygenSaturationCarriesTheVitalSignsCode()
    {
        var spo2 = First().Single(o => o.Id!.EndsWith("-spo2", StringComparison.Ordinal));

        Assert.Contains(spo2.Code.Coding, c => c.Code == "2708-6");
        Assert.Contains(spo2.Code.Coding, c => c.Code == "59408-5");
    }
}
