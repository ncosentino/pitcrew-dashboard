using Microsoft.Extensions.Configuration;

using PitCrew.Support.Agent.App;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportAgentOptionsTests
{
  [Test]
  public async Task Relay_Origin_Must_Use_Https_Except_For_Loopback()
  {
    var insecure = SupportAgentOptions.FromConfiguration(
        CreateConfiguration("http://relay.example.com"));
    var secure = SupportAgentOptions.FromConfiguration(
        CreateConfiguration("https://relay.example.com"));
    var loopback = SupportAgentOptions.FromConfiguration(
        CreateConfiguration("http://127.0.0.1:8080"));

    await Assert.That(insecure).IsNull();
    await Assert.That(secure).IsNotNull();
    await Assert.That(loopback).IsNotNull();
  }

  private static IConfiguration CreateConfiguration(string relayUrl) =>
      new ConfigurationBuilder()
          .AddInMemoryCollection(new Dictionary<string, string?>
          {
              ["PitCrewSupport:Agent:TenantId"] = "tenant-a",
              ["PitCrewSupport:Agent:NodeId"] = "11111111-1111-1111-1111-111111111111",
              ["PitCrewSupport:Agent:RelayUrl"] = relayUrl,
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
          })
          .Build();
}
