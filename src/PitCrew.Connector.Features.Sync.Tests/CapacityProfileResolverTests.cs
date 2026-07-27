using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class CapacityProfileResolverTests
{
  [Test]
  public async Task ReadCapabilityAsync_Advertises_Allowlisted_Single_Target(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          7,
          30,
          cancellationToken);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(
          CapacityTestData.CreateOperatorOptions(
              root,
              40));

      var capability = await resolver.ReadCapabilityAsync(
          cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      await Assert.That(capability.Profiles[0].ProfileId)
          .IsEqualTo("default");
      await Assert.That(capability.Profiles[0].Generation)
          .IsEqualTo(7);
      await Assert.That(capability.Profiles[0].CurrentMaximum)
          .IsEqualTo(30);
      await Assert.That(capability.Profiles[0].MaximumAllowed)
          .IsEqualTo(40);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Omits_Multi_Repository_Profile(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          7,
          30,
          cancellationToken);
      await CapacityTestData.WriteSecondRepositoryAsync(
          root,
          cancellationToken);
      var resolver = ConnectorTestFactory.CreateCapacityResolver(
          CapacityTestData.CreateOperatorOptions(root));

      var capability = await resolver.ReadCapabilityAsync(
          cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
