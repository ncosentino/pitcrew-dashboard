using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorTransportCompositionTests
{
  [Test]
  public async Task Feature_Composition_Binds_Operator_Policy_Once_And_Provides_The_Configured_Transport()
  {
    var mocks = new MockRepository(MockBehavior.Strict);
    var services = new ServiceCollection();
    services.AddLogging();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["PitCrew:Connector:DashboardUrl"] = "https://dashboard.example.test",
          ["PitCrew:Connector:DisplayName"] = "Composition test",
          ["PitCrew:Connector:StateRoot"] = Path.GetTempPath(),
          ["PitCrew:Connector:IdentityPath"] = Path.Combine(
              Path.GetTempPath(), "composition-identity.json"),
          ["PitCrew:Connector:OperatorModeEnabled"] = "true",
          ["PitCrew:Connector:PitCrewRoot"] = Path.GetTempPath(),
          ["PitCrew:Connector:AllowedCapacityProfiles:0"] = "alpha",
          ["PitCrew:Connector:ManagerRecoveryEnabled"] = "true",
          ["PitCrew:Connector:AllowedManagerRecoveryProfiles:0"] = "alpha",
          ["PitCrew:Connector:RecoveryLedgerPath"] = Path.GetTempPath(),
          ["PitCrew:Connector:ImageRolloutEnabled"] = "true",
          ["PitCrew:Connector:AllowedImageRolloutProfiles:0"] = "alpha",
          ["PitCrew:Connector:ImageRolloutStatePath"] = Path.GetTempPath(),
          ["PitCrew:Connector:ImageRolloutRecipes:0:RecipeId"] = "alpha",
          ["PitCrew:Connector:ImageRolloutRecipes:0:RegistryRepository"] =
              "ghcr.io/example/alpha",
          ["PitCrew:Connector:HttpClient:Timeout"] = "00:00:07",
        })
        .Build();
    services.AddSingleton<IConfiguration>(configuration);
    new ConnectorPlugin().Configure(new ServiceCollectionPluginOptions(
        services,
        configuration,
        [],
        mocks.Create<IPluginFactory>().Object));

    await using var provider = services.BuildServiceProvider();
    var policy = provider.GetRequiredService<IOptions<ConnectorOptions>>().Value;
    await Assert.That(policy.AllowedCapacityProfiles)
        .IsEquivalentTo(new[] { "alpha" });
    await Assert.That(policy.AllowedManagerRecoveryProfiles)
        .IsEquivalentTo(new[] { "alpha" });
    await Assert.That(policy.AllowedImageRolloutProfiles)
        .IsEquivalentTo(new[] { "alpha" });
    await Assert.That(policy.ImageRolloutRecipes.Count).IsEqualTo(1);
    await Assert.That(policy.ImageRolloutRecipes[0].RecipeId).IsEqualTo("alpha");
    await Assert.That(policy.ImageRolloutRecipes[0].RegistryRepository)
        .IsEqualTo("ghcr.io/example/alpha");
    var factory = provider.GetRequiredService<IHttpClientFactory>();
    using var client = factory.CreateClient("PitCrewConnector");
    await Assert.That(client.Timeout).IsEqualTo(TimeSpan.FromSeconds(7));
    mocks.VerifyAll();
  }
}
