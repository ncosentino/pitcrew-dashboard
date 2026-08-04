using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteDiagnosticCredentialStoreTests
{
  [Test]
  public async Task Credential_Lifecycle_Is_Tenant_Scoped_And_One_Way(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-diagnostics-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var accessStore = new SqliteAccessStore(connectionFactory);
      var store = new SqliteDiagnosticCredentialStore(
          connectionFactory);
      var now = new DateTimeOffset(
          2026,
          8,
          3,
          12,
          0,
          0,
          TimeSpan.Zero);
      var owner = new DashboardUser(
          "1",
          "owner",
          "Owner",
          null);
      await accessStore.EnsureTenantOwnerAsync(
          "tenant",
          "Tenant",
          owner,
          now,
          cancellationToken);
      await accessStore.EnsureTenantOwnerAsync(
          "other",
          "Other",
          owner,
          now,
          cancellationToken);
      var allowedNode = Guid.NewGuid();
      var otherNode = Guid.NewGuid();
      await InsertNodeAsync(
          connectionFactory,
          allowedNode,
          "tenant",
          cancellationToken);
      await InsertNodeAsync(
          connectionFactory,
          otherNode,
          "other",
          cancellationToken);

      var credentialId = Guid.NewGuid();
      const string tokenHash =
          "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
      var credential = new DiagnosticCredential(
          credentialId,
          "tenant",
          "Performance report",
          owner.GitHubUserId,
          now,
          now.AddHours(1),
          null,
          null,
          null,
          null,
          0,
          [allowedNode],
          ["default"]);
      var created = await store.CreateAsync(
          new DiagnosticCredentialWrite(
              credential,
              tokenHash),
          cancellationToken);
      var invalidNode = await store.CreateAsync(
          new DiagnosticCredentialWrite(
              credential with
              {
                CredentialId = Guid.NewGuid(),
                NodeIds = [otherNode],
              },
              new string('B', 64)),
          cancellationToken);
      var wrong = await store.ResolveOrNullAsync(
          credentialId,
          new string('C', 64),
          now.AddMinutes(1),
          cancellationToken);
      var resolved = await store.ResolveOrNullAsync(
          credentialId,
          tokenHash,
          now.AddMinutes(1),
          cancellationToken);
      var listed = await store.GetAllAsync(
          "tenant",
          cancellationToken);

      await Assert.That(created)
          .IsEqualTo(DiagnosticCredentialMutationStatus.Succeeded);
      await Assert.That(invalidNode)
          .IsEqualTo(DiagnosticCredentialMutationStatus.InvalidNode);
      await Assert.That(wrong).IsNull();
      await Assert.That(resolved).IsNotNull();
      await Assert.That(resolved!.TenantId).IsEqualTo("tenant");
      await Assert.That(resolved.NodeIds).IsEquivalentTo([allowedNode]);
      await Assert.That(resolved.ProfileIds).IsEquivalentTo(["default"]);
      await Assert.That(listed).HasSingleItem();
      await Assert.That(listed[0].LastUsedAt)
          .IsEqualTo(now.AddMinutes(1));
      await Assert.That(listed[0].UseCount).IsEqualTo(1);

      var replacementId = Guid.NewGuid();
      const string replacementHash =
          "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
      var rotated = await store.RotateAsync(
          "tenant",
          credentialId,
          replacementId,
          replacementHash,
          owner.GitHubUserId,
          now.AddMinutes(2),
          cancellationToken);
      var oldAfterRotation = await store.ResolveOrNullAsync(
          credentialId,
          tokenHash,
          now.AddMinutes(3),
          cancellationToken);
      var replacement = await store.ResolveOrNullAsync(
          replacementId,
          replacementHash,
          now.AddMinutes(3),
          cancellationToken);
      var expired = await store.ResolveOrNullAsync(
          replacementId,
          replacementHash,
          now.AddHours(2),
          cancellationToken);
      var revoked = await store.RevokeAsync(
          "tenant",
          replacementId,
          owner.GitHubUserId,
          now.AddMinutes(4),
          cancellationToken);
      var afterRevocation = await store.ResolveOrNullAsync(
          replacementId,
          replacementHash,
          now.AddMinutes(5),
          cancellationToken);

      await Assert.That(rotated.Status)
          .IsEqualTo(DiagnosticCredentialMutationStatus.Succeeded);
      await Assert.That(rotated.Credential).IsNotNull();
      await Assert.That(rotated.Credential!.RotatedFromCredentialId)
          .IsEqualTo(credentialId);
      await Assert.That(oldAfterRotation).IsNull();
      await Assert.That(replacement).IsNotNull();
      await Assert.That(expired).IsNull();
      await Assert.That(revoked)
          .IsEqualTo(DiagnosticCredentialMutationStatus.Succeeded);
      await Assert.That(afterRevocation).IsNull();
      await AssertDatabaseStoresOnlyHashesAsync(
          connectionFactory,
          tokenHash,
          replacementHash,
          cancellationToken);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static async Task InsertNodeAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO nodes (
            node_id,
            tenant_id,
            connector_instance_id,
            display_name,
            credential_hash,
            connector_version,
            enrolled_at)
        VALUES (
            $nodeId,
            $tenantId,
            $connectorInstanceId,
            $displayName,
            $credentialHash,
            '',
            '2026-08-03T12:00:00.0000000+00:00');
        """;
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$connectorInstanceId",
        Guid.NewGuid().ToString("N"));
    command.Parameters.AddWithValue(
        "$displayName",
        $"Node {nodeId:N}");
    command.Parameters.AddWithValue(
        "$credentialHash",
        Guid.NewGuid().ToString("N"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AssertDatabaseStoresOnlyHashesAsync(
      SqliteConnectionFactory connectionFactory,
      string firstHash,
      string secondHash,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT token_hash
        FROM diagnostic_credentials
        ORDER BY created_at;
        """;
    var hashes = new List<string>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      hashes.Add(reader.GetString(0));
    }
    await Assert.That(hashes)
        .IsEquivalentTo([firstHash, secondHash]);
  }
}
