using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Support.Relay.App;

namespace PitCrew.Support.Relay.App.Tests;

public sealed class RelayStoreTests
{
  [Test]
  public async Task Initialize_Upgrades_Legacy_Node_Activity_Without_Data_Loss(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var nodeId = Guid.NewGuid();
      const string tenantId = "tenant-a";
      var credentialHash = RelayCredentialHash.Hash("credential-a");
      await using (var connection = new SqliteConnection(
          $"Data Source={databasePath}"))
      {
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE relay_nodes (
                node_id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                transport_credential_hash TEXT NOT NULL UNIQUE,
                revoked_at TEXT NULL
            );
            INSERT INTO relay_nodes (
                node_id,
                tenant_id,
                transport_credential_hash,
                revoked_at)
            VALUES ($nodeId, $tenantId, $credentialHash, NULL);
            """;
        command.Parameters.AddWithValue(
            "$nodeId",
            nodeId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$tenantId", tenantId);
        command.Parameters.AddWithValue(
            "$credentialHash",
            credentialHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
      }

      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var pollAt = DateTimeOffset.Parse(
          "2026-08-01T00:01:00+00:00",
          CultureInfo.InvariantCulture);
      var poll = await store.PollAsync(
          nodeId,
          "credential-a",
          pollAt,
          cancellationToken);
      var activity = await store.GetNodeActivityAsync(
          tenantId,
          [nodeId],
          cancellationToken);

      await Assert.That(poll.CredentialAccepted).IsTrue();
      await Assert.That(activity).HasSingleItem();
      await Assert.That(activity[0].NodeId).IsEqualTo(nodeId);
      await Assert.That(activity[0].LastPollAt).IsEqualTo(pollAt);
      await Assert.That(activity[0].LastResultAt).IsNull();
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

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

      await Assert.That(wrongNode.CredentialAccepted).IsTrue();
      await Assert.That(wrongNode.Session).IsNull();
      await Assert.That(wrongCredential.CredentialAccepted).IsFalse();
      await Assert.That(wrongCredential.Session).IsNull();
      await Assert.That(result.CredentialAccepted).IsTrue();
      await Assert.That(result.Session).IsNotNull();
      await Assert.That(result.Session!.SessionId).IsEqualTo(sessionA);
      await Assert.That(result.Session.RequestEnvelope).IsEqualTo("{\"opaque\":true}");
      await Assert.That(result.Session.DispatchedAt)
          .IsEqualTo(
              DateTimeOffset.Parse(
                  "2026-08-01T00:00:00+00:00",
                  CultureInfo.InvariantCulture));
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Session_Cannot_Cross_The_Registered_Node_Tenant(
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
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeId,
              RelayCredentialHash.Hash("credential-a")),
          cancellationToken);

      var enqueued = await store.EnqueueSessionAsync(
          new RelaySessionEnqueueRequest(
              "tenant-b",
              nodeId,
              sessionId,
              DateTimeOffset.Parse("2026-08-01T00:10:00+00:00", CultureInfo.InvariantCulture),
              "opaque-request"),
          cancellationToken);
      var stored = await store.GetSessionAsync(sessionId, cancellationToken);

      await Assert.That(enqueued).IsFalse()
          .Because("relay routing must bind a session to the node's registered tenant");
      await Assert.That(stored).IsNull();
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
          DateTimeOffset.Parse(
              "2026-08-01T00:02:00+00:00",
              CultureInfo.InvariantCulture),
          cancellationToken);
      var uploaded = await store.UploadResultAsync(
          nodeId,
          sessionId,
          "credential-a",
          "opaque-result",
          DateTimeOffset.Parse(
              "2026-08-01T00:03:00+00:00",
              CultureInfo.InvariantCulture),
          cancellationToken);
      var stored = await store.GetSessionAsync(sessionId, cancellationToken);

      await Assert.That(wrongCredential)
          .IsEqualTo(RelayResultUploadOutcome.CredentialRejected);
      await Assert.That(uploaded)
          .IsEqualTo(RelayResultUploadOutcome.Succeeded);
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.Status).IsEqualTo("completed");
      await Assert.That(stored.ResultEnvelope).IsEqualTo("opaque-result");
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Poll_Marks_Expired_Session_Without_Delivering_It(
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
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeId,
              RelayCredentialHash.Hash("credential-a")),
          cancellationToken);
      await store.EnqueueSessionAsync(
          new RelaySessionEnqueueRequest(
              "tenant-a",
              nodeId,
              sessionId,
              DateTimeOffset.Parse("2026-08-01T00:01:00+00:00", CultureInfo.InvariantCulture),
              "opaque-request"),
          cancellationToken);

      var result = await store.PollAsync(
          nodeId,
          "credential-a",
          DateTimeOffset.Parse("2026-08-01T00:02:00+00:00", CultureInfo.InvariantCulture),
          cancellationToken);
      var stored = await store.GetSessionAsync(sessionId, cancellationToken);

      await Assert.That(result.CredentialAccepted).IsTrue();
      await Assert.That(result.Session).IsNull();
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.Status).IsEqualTo("expired");
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Credential_Rotation_Preserves_Old_Until_Promotion_Then_Rejects_It(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var nodeId = Guid.NewGuid();
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeId,
              RelayCredentialHash.Hash("credential-old")),
          cancellationToken);
      var rotation = new RelayNodeCredentialRotationRequest(
          Guid.NewGuid(),
          "tenant-a",
          RelayCredentialHash.Hash("credential-old"),
          RelayCredentialHash.Hash("credential-new"));
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);

      var prepared = await store.PrepareNodeCredentialAsync(
          nodeId,
          rotation,
          now,
          cancellationToken);
      var prepareRetry = await store.PrepareNodeCredentialAsync(
          nodeId,
          rotation,
          now,
          cancellationToken);
      var oldBeforePromotion = await store.PollAsync(
          nodeId,
          "credential-old",
          now,
          cancellationToken);
      var newBeforePromotion = await store.PollAsync(
          nodeId,
          "credential-new",
          now,
          cancellationToken);
      var promoted = await store.PromoteNodeCredentialAsync(
          nodeId,
          rotation,
          now.AddMinutes(1),
          cancellationToken);
      var promoteRetry = await store.PromoteNodeCredentialAsync(
          nodeId,
          rotation,
          now.AddMinutes(1),
          cancellationToken);
      var oldAfterPromotion = await store.PollAsync(
          nodeId,
          "credential-old",
          now.AddMinutes(1),
          cancellationToken);
      var newAfterPromotion = await store.PollAsync(
          nodeId,
          "credential-new",
          now.AddMinutes(1),
          cancellationToken);
      var nextRotation = new RelayNodeCredentialRotationRequest(
          Guid.NewGuid(),
          "tenant-a",
          RelayCredentialHash.Hash("credential-new"),
          RelayCredentialHash.Hash("credential-next"));
      var nextPrepared = await store.PrepareNodeCredentialAsync(
          nodeId,
          nextRotation,
          now.AddMinutes(2),
          cancellationToken);

      await Assert.That(prepared)
          .IsEqualTo(RelayCredentialRotationStatus.Prepared);
      await Assert.That(prepareRetry)
          .IsEqualTo(RelayCredentialRotationStatus.Prepared);
      await Assert.That(oldBeforePromotion.CredentialAccepted).IsTrue();
      await Assert.That(newBeforePromotion.CredentialAccepted).IsTrue();
      await Assert.That(promoted)
          .IsEqualTo(RelayCredentialRotationStatus.Promoted);
      await Assert.That(promoteRetry)
          .IsEqualTo(RelayCredentialRotationStatus.Promoted);
      await Assert.That(oldAfterPromotion.CredentialAccepted).IsFalse();
      await Assert.That(newAfterPromotion.CredentialAccepted).IsTrue();
      await Assert.That(nextPrepared)
          .IsEqualTo(RelayCredentialRotationStatus.Prepared);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Activity_Is_Tenant_Bounded_And_Monotonic(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var nodeA = Guid.NewGuid();
      var nodeB = Guid.NewGuid();
      var sessionId = Guid.NewGuid();
      var pollAt = DateTimeOffset.Parse(
          "2026-08-01T00:01:00+00:00",
          CultureInfo.InvariantCulture);
      var resultAt = pollAt.AddMinutes(1);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeA,
              RelayCredentialHash.Hash("credential-a")),
          cancellationToken);
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-b",
              nodeB,
              RelayCredentialHash.Hash("credential-b")),
          cancellationToken);
      await store.EnqueueSessionAsync(
          new RelaySessionEnqueueRequest(
              "tenant-a",
              nodeA,
              sessionId,
              resultAt.AddMinutes(5),
              "opaque-request"),
          cancellationToken);

      await store.PollAsync(
          nodeA,
          "wrong",
          pollAt.AddMinutes(1),
          cancellationToken);
      await store.PollAsync(
          nodeA,
          "credential-a",
          pollAt,
          cancellationToken);
      await store.PollAsync(
          nodeA,
          "credential-a",
          pollAt.AddMinutes(-1),
          cancellationToken);
      await store.UploadResultAsync(
          nodeA,
          sessionId,
          "credential-a",
          "opaque-result",
          resultAt,
          cancellationToken);
      await store.UploadResultAsync(
          nodeA,
          Guid.NewGuid(),
          "credential-a",
          "rejected-result",
          resultAt.AddMinutes(1),
          cancellationToken);

      var activity = await store.GetNodeActivityAsync(
          "tenant-a",
          [nodeA, nodeB],
          cancellationToken);

      await Assert.That(activity).HasSingleItem();
      await Assert.That(activity[0].NodeId).IsEqualTo(nodeA);
      await Assert.That(activity[0].LastPollAt).IsEqualTo(pollAt);
      await Assert.That(activity[0].LastResultAt).IsEqualTo(resultAt);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Revocation_Rejects_The_Next_Poll_And_Result_Exchange(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var nodeId = Guid.NewGuid();
      await store.RegisterNodeAsync(
          new RelayNodeRegistrationRequest(
              "tenant-a",
              nodeId,
              RelayCredentialHash.Hash("credential-active")),
          cancellationToken);
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      await store.RevokeNodeAsync(
          nodeId,
          now,
          cancellationToken);

      var poll = await store.PollAsync(
          nodeId,
          "credential-active",
          now,
          cancellationToken);
      var upload = await store.UploadResultAsync(
          nodeId,
          Guid.NewGuid(),
          "credential-active",
          "opaque-result",
          now,
          cancellationToken);

      await Assert.That(poll.CredentialAccepted).IsFalse();
      await Assert.That(upload)
          .IsEqualTo(RelayResultUploadOutcome.CredentialRejected);
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
