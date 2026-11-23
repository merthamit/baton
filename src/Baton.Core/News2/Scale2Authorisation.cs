namespace Baton.Core.News2;

/// <summary>
/// The recorded clinical decision that allows SpO2 Scale 2. RCP NEWS2 2017: the decision must be
/// made by a competent clinical decision maker and recorded in the patient's notes.
/// </summary>
/// <param name="AuthorisedBy">Who made the decision. Must not be empty.</param>
/// <param name="AuthorisedAt">When the decision was recorded.</param>
public sealed record Scale2Authorisation(string AuthorisedBy, DateTimeOffset AuthorisedAt);
