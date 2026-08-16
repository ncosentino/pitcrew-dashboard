using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed record SupportEnrollmentCompletion(
    SupportMutationStatus Status,
    CompletedSupportEnrollment? Enrollment);

internal sealed record CompletedSupportEnrollment(
    SupportIdentity Identity,
    string? TransportCredential,
    SupportEnvelope TransportCredentialEnvelope,
    string RelayUrl,
    string AuthorizationSigningPublicKeySpki,
    string ResultEncryptionPublicKeySpki);
