using Baton.Tests.Shared;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Baton.Scenarios.Tests;

// Age (Patient.birthDate) and diagnosis (Condition) travel through FHIR so both API sources can show them.
public class WardFhirPatientTests
{
    private static readonly DateTimeOffset Start = CardFidelityTests.Quiet.ShiftStart;
    private static readonly IReadOnlyList<WardBed> WardBeds = Ward.Generate(Start, 42);

    private static T Single<T>(WardBed bed)
        where T : Resource => WardFhirBundle.Build(bed).Entry.Select(e => e.Resource).OfType<T>().Single();

    private static int AgeAt(string birthDate, DateTimeOffset at)
    {
        var born = DateOnly.Parse(birthDate, System.Globalization.CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(at.Date);
        var age = today.Year - born.Year;
        return born.AddYears(age) > today ? age - 1 : age;
    }

    [Fact]
    public void Bundle_PatientHasBirthDateGivingAge()
    {
        Assert.All(WardBeds, bed => Assert.Equal(bed.Age, AgeAt(Single<Patient>(bed).BirthDate!, Start)));
    }

    [Fact]
    public void Bundle_HasActiveConditionWithDiagnosisText()
    {
        var bed = WardBeds[1];
        var condition = Single<Condition>(bed);

        Assert.Equal($"condition-{bed.PatientId}", condition.Id);
        Assert.Equal(bed.Diagnosis, condition.Code!.Text);
        Assert.Equal($"Patient/{bed.PatientId}", condition.Subject!.Reference);
        Assert.Equal("active", condition.ClinicalStatus!.Coding.Single().Code);
        Assert.Equal("confirmed", condition.VerificationStatus!.Coding.Single().Code);
        Assert.Equal("encounter-diagnosis", condition.Category.Single().Coding.Single().Code);
    }

    [Fact]
    public void PatientAndCondition_PassFirelyValidator()
    {
        var bed = WardBeds[1];
        foreach (Resource resource in new Resource[] { Single<Patient>(bed), Single<Condition>(bed) })
        {
            var errors = FhirValidation.Validator.Validate(resource).Issue
                .Where(i => i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal)
                .Select(i => $"{i.Severity}: {i.Details?.Text ?? i.Diagnostics} @ {string.Join(",", i.Expression)}")
                .ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
            SaveExample(resource, $"{resource.TypeName.ToLowerInvariant()}-{bed.Bed}");
        }
    }

    [Fact]
    public void StalePatientIds_ReturnsBatonPatientsNotInWard()
    {
        Assert.Equal(["synthetic-y-01"], WardFhirLoader.StalePatientIds(["synthetic-y-01", "synthetic-01"], WardBeds));
    }

    private static void SaveExample(Resource resource, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Baton.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts", "fhir-examples"));
        File.WriteAllText(Path.Combine(folder.FullName, $"{name}.json"), resource.ToJson(pretty: true));
    }
}
