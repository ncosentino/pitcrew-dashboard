using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Support.Relay.App;

namespace PitCrew.Support.Relay.App.Tests;

public sealed class RelayStoreTests
{
  [Test]
  public async Task Poll_Returns_Only_Target_Node_Opaque_Request(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var nodeA = Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture);
      var nodeB = Guid.Parse("22222222-2222-2222-2222-222222222222", CultureInfo.InvariantCulture);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest("tenant-a", nodeA, RelayCredentialHash.Hash("credential-a")),
          cancellationToken);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest("tenant-a", nodeB, RelayCredentialHash.Hash("credential-b")),
          cancellationToken);
      var sessionA = Guid.Parse("33333333-3333-3333-3333-333333333333", CultureInfo.InvariantCulture);
      await store.EnqueueSessionAsync(
          new RelaySessionEnqueueRequest(
              "tenant-a",
              nodeA,
              sessionA,
              DateTimeOffset.Parse("2026-08-01T00:10:00+00:00", CultureInfo.InvariantCulture),
              "{\"opaque\":true}"),
          cancellationToken);

      var wrongNode = await store.PollAsync(
          nodeB,
          "credential-b",
          DateTimeOffset.Parse("2026-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          cancellationToken);
      var wrongCredential = await store.PollAsync(
          nodeA,
          "credential-b",
          DateTimeOffset.Parse("2026-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          cancellationToken);
      var result = await store.PollAsync(
          nodeA,
          "credential-a",
          DateTimeOffset.Parse("2026-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          cancellationToken);

      await Assert.That(wrongNode).IsNull();
      await Assert.That(wrongCredential).IsNull();
      await Assert.That(result).IsNotNull();
      await Assert.That(result!.SessionId).IsEqualTo(sessionA);
      await Assert.That(result.RequestEnvelope).IsEqualTo("{\"opaque\":true}");
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Result_Upload_Is_Node_Bound_And_Relay_Stores_Opaque_Result(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture);
      var sessionId = Guid.Parse("33333333-3333-3333-3333-333333333333", CultureInfo.InvariantCulture);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest("tenant-a", nodeId, RelayCredentialHash.Hash("credential-a")),
          cancellationToken);
      await store.EnqueueSessionAsync(
          new RelaySessionEnqueueRequest(
              "tenant-a",
              nodeId,
              sessionId,
              DateTimeOffset.Parse("2026-08-01T00:10:00+00:00", CultureInfo.InvariantCulture),
              "opaque-request"),
          cancellationToken);

      var wrongCredential = await store.UploadResultAsync(
          nodeId,
          sessionId,
          "wrong",
          "opaque-result",
          cancellationToken);
      var uploaded = await store.UploadResultAsync(
          nodeId,
          sessionId,
          "credential-a",
          "opaque-result",
          cancellationToken);
      var stored = await store.GetSessionAsync(sessionId, cancellationToken);

      await Assert.That(wrongCredential).IsFalse().Because("node bearer authentication precedes result upload");
      await Assert.That(uploaded).IsTrue().Because("the correct node credential may upload its result");
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.Status).IsEqualTo("completed");
      await Assert.That(stored.ResultEnvelope).IsEqualTo("opaque-result");
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  private static string CreateDatabasePath() =>
      Path.Combine(
          AppContext.BaseDirectory,
          $"support-relay-{Guid.NewGuid():N}.db");

  private static void DeleteDatabase(string databasePath)
  {
    SqliteConnection.ClearAllPools();
    foreach (var path in new[]
    {
        databasePath,
        $"{databasePath}-shm",
        $"{databasePath}-wal",
    })
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
  }
}




