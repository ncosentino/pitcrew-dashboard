namespace PitCrew.Support.Protocol;

/// <summary>
/// Wire response returned after successful support-identity rotation.
/// </summary>
/// <param name="NodeId">Dashboard-assigned support node identifier.</param>
/// <param name="DisplayName">Operator-facing support node label.</param>
/// <param name="TransportCredential">Accepted replacement relay credential.</param>
/// <param name="RelayUrl">Relay base URL.</param>
/// <param name="AuthorizationSigningPublicKeySpki">Dashboard request-signing public SPKI.</param>
/// <param name="ResultEncryptionPublicKeySpki">Dashboard result-encryption public SPKI.</param>
public sealed record SupportIdentityCompletionResponse(
    string NodeId,
    string DisplayName,
    string TransportCredential,
    string RelayUrl,
    string AuthorizationSigningPublicKeySpki,
    string ResultEncryptionPublicKeySpki);
