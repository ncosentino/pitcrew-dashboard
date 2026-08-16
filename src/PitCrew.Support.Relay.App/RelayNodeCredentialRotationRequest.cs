namespace PitCrew.Support.Relay.App;

internal sealed record RelayNodeCredentialRotationRequest(
    Guid RotationId,
    string TenantId,
    string ExpectedTransportCredentialHash,
    string ReplacementTransportCredentialHash);

internal enum RelayCredentialRotationStatus
{
  Authorized,
  Prepared,
  Promoted,
  NotFound,
  Revoked,
  Forbidden,
  Conflict,
}
