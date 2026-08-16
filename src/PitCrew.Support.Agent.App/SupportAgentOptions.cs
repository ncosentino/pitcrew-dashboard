using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace PitCrew.Support.Agent.App;

internal sealed record SupportAgentOptions(
    string TenantId,
    Guid NodeId,
    Uri RelayUrl,
    string TransportCredential,
    string DashboardAuthorizationSigningPublicKeySpki,
    string DashboardResultEncryptionPublicKeySpki,
    string NodeSigningPrivateKeyPkcs8,
    string NodeEncryptionPrivateKeyPkcs8,
    string ReplayRoot,
    string PipeName)
{
  public static SupportAgentOptions? FromConfiguration(IConfiguration configuration)
  {
    var tenantId = configuration["PitCrewSupport:Agent:TenantId"];
    var nodeIdValue = configuration["PitCrewSupport:Agent:NodeId"];
    var relayUrlValue = configuration["PitCrewSupport:Agent:RelayUrl"];
    var transportCredential = configuration["PitCrewSupport:Agent:TransportCredential"];
    var authPublicKey = configuration["PitCrewSupport:Agent:DashboardAuthorizationSigningPublicKeySpki"];
    var resultPublicKey = configuration["PitCrewSupport:Agent:DashboardResultEncryptionPublicKeySpki"];
    var signingPrivateKey = configuration["PitCrewSupport:Agent:NodeSigningPrivateKeyPkcs8"];
    var encryptionPrivateKey = configuration["PitCrewSupport:Agent:NodeEncryptionPrivateKeyPkcs8"];
    if (string.IsNullOrWhiteSpace(tenantId) ||
        !Guid.TryParse(nodeIdValue, CultureInfo.InvariantCulture, out var nodeId) ||
        !Uri.TryCreate(relayUrlValue, UriKind.Absolute, out var relayUrl) ||
        string.IsNullOrWhiteSpace(transportCredential) ||
        string.IsNullOrWhiteSpace(authPublicKey) ||
        string.IsNullOrWhiteSpace(resultPublicKey) ||
        string.IsNullOrWhiteSpace(signingPrivateKey) ||
        string.IsNullOrWhiteSpace(encryptionPrivateKey))
    {
      return null;
    }
    return new SupportAgentOptions(
        tenantId,
        nodeId,
        relayUrl,
        transportCredential,
        authPublicKey,
        resultPublicKey,
        signingPrivateKey,
        encryptionPrivateKey,
        configuration["PitCrewSupport:Agent:ReplayRoot"] ?? "support-security-state",
        configuration["PitCrewSupport:Agent:PipeName"] ?? "pitcrew-support-broker-v1");
  }
}


