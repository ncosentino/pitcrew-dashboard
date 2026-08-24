namespace PitCrew.Support.Protocol;

/// <summary>
/// Reports one bounded request rejection to the authenticated relay session.
/// </summary>
/// <param name="Disposition">Closed rejection disposition.</param>
public sealed record SupportRelayRequestOutcomeRequest(
    string Disposition);
