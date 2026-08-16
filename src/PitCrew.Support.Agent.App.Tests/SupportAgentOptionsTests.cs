using Microsoft.Extensions.Configuration;

using PitCrew.Support.Agent.App;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportAgentOptionsTests
{
  [Test]
  public async Task Dashboard_Origin_Must_Use_Https_Except_For_Loopback()
  {
    var insecure = SupportAgentBootstrapOptions.FromConfiguration(
        CreateConfiguration("http://dashboard.example.com", allowLegacy: false));
    var secure = SupportAgentBootstrapOptions.FromConfiguration(
        CreateConfiguration("https://dashboard.example.com", allowLegacy: false));
    var loopback = SupportAgentBootstrapOptions.FromConfiguration(
        CreateConfiguration("http://127.0.0.1:8080", allowLegacy: false));

    await Assert.That(insecure).IsNull();
    await Assert.That(secure).IsNotNull();
    await Assert.That(loopback).IsNotNull();
  }

  [Test]
  public async Task Legacy_Private_Key_Configuration_Requires_Explicit_Gate()
  {
    var disabledConfiguration = CreateConfiguration(
        "https://dashboard.example.com",
        allowLegacy: false);
    var enabledConfiguration = CreateConfiguration(
        "https://dashboard.example.com",
        allowLegacy: true);
    var disabled = SupportAgentBootstrapOptions
        .FromConfiguration(disabledConfiguration)!
        .CreateLegacyOrNull(disabledConfiguration);
    var enabled = SupportAgentBootstrapOptions
        .FromConfiguration(enabledConfiguration)!
        .CreateLegacyOrNull(enabledConfiguration);

    await Assert.That(disabled).IsNull();
    await Assert.That(enabled).IsNotNull();
  }

  private static IConfiguration CreateConfiguration(
      string dashboardUrl,
      bool allowLegacy) =>
      new ConfigurationBuilder()
          .AddInMemoryCollection(new Dictionary<string, string?>
          {
              ["PitCrewSupport:Agent:DashboardUrl"] = dashboardUrl,
              ["PitCrewSupport:Agent:TenantId"] = "tenant-a",
              ["PitCrewSupport:Agent:NodeId"] =
                  "11111111-1111-1111-1111-111111111111",
              ["PitCrewSupport:Agent:RelayUrl"] = "https://relay.example.com",
              ["PitCrewSupport:Agent:TransportCredential"] =
                  "fixture-transport-credential",
              ["PitCrewSupport:Agent:DashboardAuthorizationSigningPublicKeySpki"] =
                  "fixture-auth-key",
              ["PitCrewSupport:Agent:DashboardResultEncryptionPublicKeySpki"] =
                  "fixture-result-key",
              ["PitCrewSupport:Agent:NodeSigningPrivateKeyPkcs8"] =
                  "fixture-signing-key",
              ["PitCrewSupport:Agent:NodeEncryptionPrivateKeyPkcs8"] =
                  "fixture-encryption-key",
              ["PitCrewSupport:Agent:AllowLegacyPrivateKeyConfiguration"] =
                  allowLegacy.ToString(),
          })
          .Build();
}
