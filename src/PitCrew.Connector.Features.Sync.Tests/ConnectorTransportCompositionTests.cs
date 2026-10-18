using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorTransportCompositionTests
{
  [Test]
  public async Task Feature_Composition_Provides_The_Configured_Outbound_Transport()
  {
    var mocks = new MockRepository(MockBehavior.Strict);
    var services = new ServiceCollection();
    services.AddLogging();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
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
    var factory = provider.GetRequiredService<IHttpClientFactory>();
    using var client = factory.CreateClient("PitCrewConnector");
    await Assert.That(client.Timeout).IsEqualTo(TimeSpan.FromSeconds(7));
    mocks.VerifyAll();
  }
}
