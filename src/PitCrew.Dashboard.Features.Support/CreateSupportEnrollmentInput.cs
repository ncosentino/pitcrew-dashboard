namespace PitCrew.Dashboard.Features.Support;

internal sealed record CreateSupportEnrollmentInput(
    string DisplayName,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);
