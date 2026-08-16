namespace PitCrew.Support.Protocol;

/// <summary>
/// Node-signed canonical result payload carried inside the encrypted result envelope.
/// </summary>
/// <param name="PayloadBase64Url">Canonical UTF-8 JSON result payload.</param>
/// <param name="SignatureBase64Url">ECDSA P-256 IEEE-P1363 signature over the payload.</param>
public sealed record SupportSignedResultPackage(
    string PayloadBase64Url,
    string SignatureBase64Url);
