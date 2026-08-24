using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Support.Relay.App;

namespace PitCrew.Support.Relay.App.Tests;

public sealed class RelaySessionLifecycleStoreTests
{
  [Test]
  public async Task Initialize_Upgrades_Legacy_Session_Lifecycle_Without_Data_Loss(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var nodeId = Guid.NewGuid();
      var sessionId = Guid.NewGuid();
      var expiresAt = DateTimeOffset.Parse(
          "2026-08-01T00:10:00+00:00",
          CultureInfo.InvariantCulture);
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
            CREATE TABLE relay_sessions (
                session_id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                node_id TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN (
                    'queued',
                    'dispatched',
                    'completed',
                    'cancelled',
                    'expired')),
                expires_at TEXT NOT NULL,
                request_envelope_json TEXT NOT NULL,
                result_envelope_json TEXT NULL,
                FOREIGN KEY (node_id) REFERENCES relay_nodes(node_id)
            );
            CREATE INDEX ix_relay_sessions_node_status_expiry
                ON relay_sessions (
                    node_id,
                    status,
                    expires_at,
                    session_id);
            INSERT INTO relay_nodes (
                node_id,
                tenant_id,
                transport_credential_hash,
                revoked_at)
            VALUES ($nodeId, 'tenant-a', $credentialHash, NULL);
            INSERT INTO relay_sessions (
                session_id,
                tenant_id,
                node_id,
                status,
                expires_at,
                request_envelope_json,
                result_envelope_json)
            VALUES (
                $sessionId,
                'tenant-a',
                $nodeId,
                'queued',
                $expiresAt,
                'opaque-request',
                NULL);
            """;
        command.Parameters.AddWithValue(
            "$nodeId",
            nodeId.ToString("D"));
        command.Parameters.AddWithValue(
            "$sessionId",
            sessionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$credentialHash",
            RelayCredentialHash.Hash("credential-a"));
        command.Parameters.AddWithValue(
            "$expiresAt",
            expiresAt.ToString(
                "O",
                CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
      }

      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var rejectedAt = expiresAt.AddMinutes(-5);
      var outcome = await store.ReportRequestOutcomeAsync(
          nodeId,
          sessionId,
          "credential-a",
          "unsupported-capability",
          rejectedAt,
          cancellationToken);
      var stored = await store.GetSessionAsync(
          sessionId,
          cancellationToken);

      await Assert.That(outcome)
          .IsEqualTo(RelayRequestOutcomeStatus.Succeeded);
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.RequestEnvelope)
          .IsEqualTo("opaque-request");
      await Assert.That(stored.Status).IsEqualTo("rejected");
      await Assert.That(stored.DispatchedAt)
          .IsEqualTo(rejectedAt);
      await Assert.That(stored.RejectedAt)
          .IsEqualTo(rejectedAt);
      await Assert.That(stored.RejectionDisposition)
          .IsEqualTo("unsupported-capability");
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Rejection_Is_Authenticated_Idempotent_And_Terminal(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var store = new SqliteRelayStore(databasePath);
      await store.InitializeAsync(cancellationToken);
      var nodeId = Guid.NewGuid();
      var sessionId = Guid.NewGuid();
      var pollAt = DateTimeOffset.Parse(
          "2026-08-01T00:01:00+00:00",
          CultureInfo.InvariantCulture);
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
              pollAt.AddMinutes(10),
              "opaque-request"),
          cancellationToken);
      await store.PollAsync(
          nodeId,
          "credential-a",
          pollAt,
          cancellationToken);

      var unauthorized =
          await store.ReportRequestOutcomeAsync(
              nodeId,
              sessionId,
              "wrong",
              "request-malformed",
              pollAt.AddSeconds(1),
              cancellationToken);
      var rejected = await store.ReportRequestOutcomeAsync(
          nodeId,
          sessionId,
          "credential-a",
          "request-malformed",
          pollAt.AddSeconds(1),
          cancellationToken);
      var retry = await store.ReportRequestOutcomeAsync(
          nodeId,
          sessionId,
          "credential-a",
          "request-malformed",
          pollAt.AddSeconds(2),
          cancellationToken);
      var conflict = await store.ReportRequestOutcomeAsync(
          nodeId,
          sessionId,
          "credential-a",
          "unsupported-capability",
          pollAt.AddSeconds(3),
          cancellationToken);
      var result = await store.UploadResultAsync(
          nodeId,
          sessionId,
          "credential-a",
          "opaque-result",
          pollAt.AddSeconds(4),
          cancellationToken);
      var stored = await store.GetSessionAsync(
          sessionId,
          cancellationToken);

      await Assert.That(unauthorized)
          .IsEqualTo(
              RelayRequestOutcomeStatus.CredentialRejected);
      await Assert.That(rejected)
          .IsEqualTo(RelayRequestOutcomeStatus.Succeeded);
      await Assert.That(retry)
          .IsEqualTo(RelayRequestOutcomeStatus.Succeeded);
      await Assert.That(conflict)
          .IsEqualTo(RelayRequestOutcomeStatus.Conflict);
      await Assert.That(result)
          .IsEqualTo(RelayResultUploadOutcome.SessionRejected);
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.Status).IsEqualTo("rejected");
      await Assert.That(stored.DispatchedAt)
          .IsEqualTo(pollAt);
      await Assert.That(stored.RejectedAt)
          .IsEqualTo(pollAt.AddSeconds(1));
      await Assert.That(stored.RejectionDisposition)
          .IsEqualTo("request-malformed");
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  private static string CreateDatabasePath() =>
      Path.Combine(
          AppContext.BaseDirectory,
          $"support-relay-lifecycle-{Guid.NewGuid():N}.db");

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
