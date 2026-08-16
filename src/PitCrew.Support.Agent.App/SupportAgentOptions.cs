namespace PitCrew.Support.Agent.App;

internal sealed record SupportAgentOptions(
    string TenantId,
    Guid NodeId,
    Uri DashboardUrl,
    Uri RelayUrl,
    string TransportCredential,
    string DashboardAuthorizationSigningPublicKeySpki,
    string DashboardResultEncryptionPublicKeySpki,
    string ReplayRoot,
    string PipeName,
    string SocketPath,
    ISupportNodePrivateKeySource PrivateKeys)
{
  public static SupportAgentOptions FromStoredIdentity(
      StoredSupportNodeIdentity identity,
      string socketPath) =>
      new(
          identity.TenantId,
          identity.NodeId,
          identity.DashboardUrl,
          identity.RelayUrl,
          identity.TransportCredential,
          identity.DashboardAuthorizationSigningPublicKeySpki,
          identity.DashboardResultEncryptionPublicKeySpki,
          identity.ReplayRoot,
          identity.PipeName,
          socketPath,
          identity);
}
