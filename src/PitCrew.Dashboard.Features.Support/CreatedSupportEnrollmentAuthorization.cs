namespace PitCrew.Dashboard.Features.Support;

internal sealed record CreatedSupportEnrollmentAuthorization(
    string DisplayName,
    string EnrollmentCode,
    DateTimeOffset ExpiresAt);
