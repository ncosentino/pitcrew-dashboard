using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorIdentityStoreTests
{
  [Test]
  public async Task SaveAsync_Replaces_Pending_Identity(
      CancellationToken cancellationToken)
  {
    var root = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-identity-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      var identityPath = Path.Combine(root, "identity.json");
      var store = new ConnectorIdentityStore(
          Options.Create(
              ConnectorTestData.CreateOptions(
                  root,
                  identityPath)));
      var pending = await store.LoadOrCreatePendingAsync(
          cancellationToken);
      var enrolled = pending with
      {
        NodeId = Guid.NewGuid(),
        Credential = "node-credential",
      };

      await store.SaveAsync(
          enrolled,
          cancellationToken);
      var loaded = await store.LoadOrCreatePendingAsync(
          cancellationToken);

      await Assert.That(loaded).IsEqualTo(enrolled);
      await Assert.That(Directory.GetFiles(root, "*.tmp"))
          .IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
