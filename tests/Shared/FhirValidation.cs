using Firely.Fhir.Validation;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;

namespace Baton.Tests.Shared;

/// <summary>
/// The Firely validator for test projects. Each test assembly unzips the FHIR specification into its own folder:
/// the default shared folder made two test projects running in parallel collide ("Directory not empty").
/// </summary>
internal static class FhirValidation
{
    private static readonly Lazy<Validator> SharedValidator = new(() =>
    {
        var zip = Path.Combine(AppContext.BaseDirectory, "specification.zip");
        var folder = Path.Combine(Path.GetTempPath(), "baton-fhir-spec", typeof(FhirValidation).Assembly.GetName().Name!);
        var resolver = new CachedResolver(new ZipSource(zip, folder));
        return new Validator(resolver, new LocalTerminologyService(resolver));
    });

    public static Validator Validator => SharedValidator.Value;
}
