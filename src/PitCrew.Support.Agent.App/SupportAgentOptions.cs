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
        !IsAllowedRelayOrigin(relayUrl) ||
        transportCredential is null ||
        transportCredential.Length is < 16 or > 4096 ||
        transportCredential.Contains('\r') ||
        transportCredential.Contains('\n') ||
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
        configuration["PitCrewSupport:Agent:ReplayRoot"] ?? DefaultReplayRoot(),
        configuration["PitCrewSupport:Agent:PipeName"] ?? "pitcrew-support-broker-v1");
  }

  private static string DefaultReplayRoot() =>
      OperatingSystem.IsWindows()
          ? Path.Combine(
              Environment.GetFolderPath(
                  Environment.SpecialFolder.CommonApplicationData),
              "PitCrew",
              "Support")
          : "/var/lib/pitcrew-support";

  private static bool IsAllowedRelayOrigin(Uri relayUrl) =>
      string.IsNullOrEmpty(relayUrl.UserInfo) &&
      string.IsNullOrEmpty(relayUrl.Query) &&
      string.IsNullOrEmpty(relayUrl.Fragment) &&
      relayUrl.AbsolutePath == "/" &&
      (relayUrl.Scheme == Uri.UriSchemeHttps ||
       relayUrl.Scheme == Uri.UriSchemeHttp && relayUrl.IsLoopback);
}
