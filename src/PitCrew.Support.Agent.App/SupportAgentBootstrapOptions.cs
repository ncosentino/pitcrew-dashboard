using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace PitCrew.Support.Agent.App;

internal sealed record SupportAgentBootstrapOptions(
    string IdentityRoot,
    string ReplayRoot,
    string PipeName,
    string SocketPath,
    Uri? DashboardUrl,
    string? TenantId,
    string? DisplayName,
    string? EnrollmentCode,
    bool AllowLegacyPrivateKeyConfiguration)
{
  public static SupportAgentBootstrapOptions? FromConfiguration(
      IConfiguration configuration)
  {
    var identityRoot = configuration["PitCrewSupport:Agent:IdentityRoot"] ??
        Path.Combine(DefaultSupportRoot(), "identity");
    var replayRoot = configuration["PitCrewSupport:Agent:ReplayRoot"] ??
        Path.Combine(DefaultSupportRoot(), "replay");
    var pipeName = configuration["PitCrewSupport:Agent:PipeName"] ??
        "pitcrew-support-broker-v1";
    var socketPath = configuration["PitCrewSupport:Agent:SocketPath"] ??
        "/run/pitcrew-support/broker.sock";
    var dashboardUrlValue = configuration["PitCrewSupport:Agent:DashboardUrl"];
    Uri? dashboardUrl = null;
    if (!string.IsNullOrWhiteSpace(dashboardUrlValue) &&
        (!Uri.TryCreate(
            dashboardUrlValue,
            UriKind.Absolute,
            out dashboardUrl) ||
         !IsAllowedOrigin(dashboardUrl)))
    {
      return null;
    }
    if (!Path.IsPathFullyQualified(identityRoot) ||
        !Path.IsPathFullyQualified(replayRoot) ||
        string.IsNullOrWhiteSpace(pipeName) ||
        pipeName.Length > 128 ||
        string.IsNullOrWhiteSpace(socketPath) ||
        socketPath.Length > 512 ||
        OperatingSystem.IsLinux() &&
        !Path.IsPathFullyQualified(socketPath))
    {
      return null;
    }
    return new SupportAgentBootstrapOptions(
        identityRoot,
        replayRoot,
        pipeName,
        socketPath,
        dashboardUrl,
        configuration["PitCrewSupport:Agent:TenantId"],
        configuration["PitCrewSupport:Agent:DisplayName"],
        configuration["PitCrewSupport:Agent:EnrollmentCode"],
        bool.TryParse(
            configuration[
                "PitCrewSupport:Agent:AllowLegacyPrivateKeyConfiguration"],
            out var allowLegacy) &&
        allowLegacy);
  }

  public SupportAgentOptions? CreateLegacyOrNull(
      IConfiguration configuration)
  {
    if (!AllowLegacyPrivateKeyConfiguration)
    {
      return null;
    }
    var tenantId = configuration["PitCrewSupport:Agent:TenantId"];
    var nodeIdValue = configuration["PitCrewSupport:Agent:NodeId"];
    var relayUrlValue = configuration["PitCrewSupport:Agent:RelayUrl"];
    var transportCredential =
        configuration["PitCrewSupport:Agent:TransportCredential"];
    var authPublicKey = configuration[
        "PitCrewSupport:Agent:DashboardAuthorizationSigningPublicKeySpki"];
    var resultPublicKey = configuration[
        "PitCrewSupport:Agent:DashboardResultEncryptionPublicKeySpki"];
    var signingPrivateKey = configuration[
        "PitCrewSupport:Agent:NodeSigningPrivateKeyPkcs8"];
    var encryptionPrivateKey = configuration[
        "PitCrewSupport:Agent:NodeEncryptionPrivateKeyPkcs8"];
    if (DashboardUrl is null ||
        string.IsNullOrWhiteSpace(tenantId) ||
        !Guid.TryParse(
            nodeIdValue,
            CultureInfo.InvariantCulture,
            out var nodeId) ||
        !Uri.TryCreate(relayUrlValue, UriKind.Absolute, out var relayUrl) ||
        !IsAllowedOrigin(relayUrl) ||
        !IsCredentialValid(transportCredential) ||
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
        DashboardUrl,
        relayUrl,
        transportCredential!,
        authPublicKey!,
        resultPublicKey!,
        ReplayRoot,
        PipeName,
        SocketPath,
        new LegacySupportNodePrivateKeySource(
            signingPrivateKey!,
            encryptionPrivateKey!));
  }

  public bool HasEnrollmentMaterial =>
      DashboardUrl is not null &&
      !string.IsNullOrWhiteSpace(TenantId) &&
      TenantId.Length <= 128 &&
      !string.IsNullOrWhiteSpace(DisplayName) &&
      DisplayName.Length <= 128 &&
      EnrollmentCode is { Length: >= 32 and <= 256 };

  private static bool IsCredentialValid(string? credential) =>
      credential is { Length: >= 16 and <= 4096 } &&
      !credential.Contains('\r') &&
      !credential.Contains('\n');

  private static string DefaultSupportRoot() =>
      OperatingSystem.IsWindows()
          ? Path.Combine(
              Environment.GetFolderPath(
                  Environment.SpecialFolder.CommonApplicationData),
              "PitCrew",
              "Support")
          : "/var/lib/pitcrew-support";

  internal static bool IsAllowedOrigin(Uri uri) =>
      string.IsNullOrEmpty(uri.UserInfo) &&
      string.IsNullOrEmpty(uri.Query) &&
      string.IsNullOrEmpty(uri.Fragment) &&
      uri.AbsolutePath == "/" &&
      (uri.Scheme == Uri.UriSchemeHttps ||
       uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
