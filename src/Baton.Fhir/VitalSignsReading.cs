using Baton.Core.News2;
using Hl7.Fhir.Model;

namespace Baton.Fhir;

/// <summary>The result of reading vital signs from FHIR.</summary>
/// <param name="Input">The NEWS2 input. A parameter whose reading could not be used is null.</param>
/// <param name="Sources">The Observation each used value came from.</param>
/// <param name="Notes">Why a reading was not used, and any conversion or rounding applied.</param>
public sealed record VitalSignsReading(
    News2Input Input,
    IReadOnlyDictionary<News2Parameter, Observation> Sources,
    IReadOnlyList<string> Notes);
