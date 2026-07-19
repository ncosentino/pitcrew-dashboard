using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ObservedStateReaderTests
{
  [Test]
  public async Task ReadAsync_Retains_Last_Good_State_And_Detects_Removal(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      Directory.CreateDirectory(Path.Combine(root, "legacy-profile"));
      var observedStatePath = Path.Combine(
          profileDirectory.FullName,
          "observed-state.json");
      var observedState = ConnectorTestData.CreateObservedState(
          "default",
          DateTimeOffset.UtcNow);
      await File.WriteAllTextAsync(
          observedStatePath,
          JsonSerializer.Serialize(
              observedState,
              PitCrewProtocolJsonContext.Default.ManagerObservedState),
          cancellationToken);
      var options = Options.Create(
          ConnectorTestData.CreateOptions(
              root,
              Path.Combine(root, "identity.json")));
      var reader = new ObservedStateReader(
          options,
          NullLogger<ObservedStateReader>.Instance);

      var initial = await reader.ReadAsync(cancellationToken);
      await Assert.That(initial.IsComplete).IsTrue();
      await Assert.That(initial.Profiles).HasSingleItem();
      await Assert.That(initial.AggregateHash).IsNotEmpty();

      await File.WriteAllTextAsync(
          observedStatePath,
          "{",
          cancellationToken);
      var retained = await reader.ReadAsync(cancellationToken);
      await Assert.That(retained.IsComplete).IsTrue();
      await Assert.That(retained.AggregateHash)
          .IsEqualTo(initial.AggregateHash);
      await Assert.That(retained.Profiles[0])
          .IsEqualTo(initial.Profiles[0]);

      Directory.Delete(profileDirectory.FullName, true);
      var removed = await reader.ReadAsync(cancellationToken);
      await Assert.That(removed.IsComplete).IsTrue();
      await Assert.That(removed.Profiles).IsEmpty();
      await Assert.That(removed.AggregateHash)
          .IsNotEqualTo(initial.AggregateHash);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadAsync_Rejects_Oversized_First_Snapshot(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var profileDirectory = Directory.CreateDirectory(
          Path.Combine(root, "default"));
      await File.WriteAllTextAsync(
          Path.Combine(
              profileDirectory.FullName,
              "observed-state.json"),
          new string('x', 2048),
          cancellationToken);
      var connectorOptions = ConnectorTestData.CreateOptions(
          root,
          Path.Combine(root, "identity.json"));
      connectorOptions.MaximumObservedStateBytes = 1024;
      var reader = new ObservedStateReader(
          Options.Create(connectorOptions),
          NullLogger<ObservedStateReader>.Instance);

      var result = await reader.ReadAsync(cancellationToken);

      await Assert.That(result.IsComplete).IsFalse();
      await Assert.That(result.Profiles).IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
  }
}
