namespace PitCrew.Dashboard.Features.Support;

internal sealed record CompleteSupportEnrollmentInput(
    string TenantId,
    string EnrollmentCode,
    Guid CompletionId,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);
