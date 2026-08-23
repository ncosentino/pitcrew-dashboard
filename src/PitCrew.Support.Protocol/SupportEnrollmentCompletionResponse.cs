namespace PitCrew.Support.Protocol;

/// <summary>
/// Wire response returned after successful support-node enrollment.
/// </summary>
/// <param name="NodeId">Dashboard-assigned support node identifier.</param>
/// <param name="DisplayName">Operator-facing support node label.</param>
/// <param name="TransportCredentialEnvelope">Credential encrypted to the node RSA key.</param>
/// <param name="RelayUrl">Relay base URL.</param>
/// <param name="AuthorizationSigningPublicKeySpki">Dashboard request-signing public SPKI.</param>
/// <param name="ResultEncryptionPublicKeySpki">Dashboard result-encryption public SPKI.</param>
public sealed record SupportEnrollmentCompletionResponse(
    string NodeId,
    string DisplayName,
    SupportEnvelope TransportCredentialEnvelope,
    string RelayUrl,
    string AuthorizationSigningPublicKeySpki,
    string ResultEncryptionPublicKeySpki);
