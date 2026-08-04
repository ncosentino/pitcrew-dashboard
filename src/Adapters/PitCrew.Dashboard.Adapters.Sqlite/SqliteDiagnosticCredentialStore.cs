using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteDiagnosticCredentialStore(
    SqliteConnectionFactory _connectionFactory) :
    IDiagnosticCredentialStore
{
  public async Task<DiagnosticCredentialMutationStatus> CreateAsync(
      DiagnosticCredentialWrite write,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var status = await InsertAsync(
        connection,
        transaction,
        write,
        cancellationToken);
    if (status == DiagnosticCredentialMutationStatus.Succeeded)
    {
      await transaction.CommitAsync(cancellationToken);
    }
    else
    {
      await transaction.RollbackAsync(cancellationToken);
    }
    return status;
  }

  public async Task<IReadOnlyList<DiagnosticCredential>> GetAllAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        CredentialProjection + "\n" +
        """
        WHERE c.tenant_id = $tenantId
        ORDER BY c.created_at DESC, c.credential_id;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    var credentials = new List<DiagnosticCredential>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      credentials.Add(ReadCredential(new SqliteRowReader(reader)));
    }
    return credentials;
  }

  public async Task<DiagnosticAccessScope?> ResolveOrNullAsync(
      Guid credentialId,
      string tokenHash,
      DateTimeOffset usedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE diagnostic_credentials
        SET last_used_at = $usedAt,
            use_count = use_count + 1
        WHERE credential_id = $credentialId
          AND token_hash = $tokenHash
          AND revoked_at IS NULL
          AND expires_at > $usedAt
        RETURNING
            credential_id,
            tenant_id,
            COALESCE((
                SELECT group_concat(n.node_id, ',')
                FROM diagnostic_credential_nodes AS n
                WHERE n.credential_id =
                    diagnostic_credentials.credential_id
                ORDER BY n.node_id), '') AS node_ids,
            COALESCE((
                SELECT group_concat(p.profile_id, ',')
                FROM diagnostic_credential_profiles AS p
                WHERE p.credential_id =
                    diagnostic_credentials.credential_id
                ORDER BY p.profile_id), '') AS profile_ids;
        """;
    command.Parameters.AddWithValue(
        "$credentialId",
        FormatGuid(credentialId));
    command.Parameters.AddWithValue("$tokenHash", tokenHash);
    command.Parameters.AddWithValue("$usedAt", FormatTimestamp(usedAt));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }
    var row = new SqliteRowReader(reader);
    return new DiagnosticAccessScope(
        Guid.Parse(
            row.String("credential_id"),
            CultureInfo.InvariantCulture),
        row.String("tenant_id"),
        ParseGuids(row.String("node_ids")),
        ParseStrings(row.String("profile_ids")));
  }

  public async Task<DiagnosticCredentialMutationStatus> RevokeAsync(
      string tenantId,
      Guid credentialId,
      string revokedByGitHubUserId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE diagnostic_credentials
        SET revoked_at = $revokedAt,
            revoked_by_github_user_id = $revokedByGitHubUserId
        WHERE tenant_id = $tenantId
          AND credential_id = $credentialId
          AND revoked_at IS NULL;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$credentialId",
        FormatGuid(credentialId));
    command.Parameters.AddWithValue(
        "$revokedByGitHubUserId",
        revokedByGitHubUserId);
    command.Parameters.AddWithValue(
        "$revokedAt",
        FormatTimestamp(revokedAt));
    if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
    {
      return DiagnosticCredentialMutationStatus.Succeeded;
    }
    return await ExistsAsync(
        connection,
        tenantId,
        credentialId,
        cancellationToken)
        ? DiagnosticCredentialMutationStatus.Conflict
        : DiagnosticCredentialMutationStatus.NotFound;
  }

  public async Task<DiagnosticCredentialMutation> RotateAsync(
      string tenantId,
      Guid credentialId,
      Guid replacementCredentialId,
      string replacementTokenHash,
      string rotatedByGitHubUserId,
      DateTimeOffset rotatedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var current = await GetActiveOrNullAsync(
        connection,
        transaction,
        tenantId,
        credentialId,
        rotatedAt,
        cancellationToken);
    if (current is null)
    {
      await transaction.RollbackAsync(cancellationToken);
      var exists = await ExistsAsync(
          connection,
          tenantId,
          credentialId,
          cancellationToken);
      return new DiagnosticCredentialMutation(
          exists
              ? DiagnosticCredentialMutationStatus.Conflict
              : DiagnosticCredentialMutationStatus.NotFound,
          null);
    }
    var replacement = current with
    {
      CredentialId = replacementCredentialId,
      CreatedByGitHubUserId = rotatedByGitHubUserId,
      CreatedAt = rotatedAt,
      RevokedAt = null,
      RevokedByGitHubUserId = null,
      RotatedFromCredentialId = credentialId,
      LastUsedAt = null,
      UseCount = 0,
    };
    var createStatus = await InsertAsync(
        connection,
        transaction,
        new DiagnosticCredentialWrite(
            replacement,
            replacementTokenHash),
        cancellationToken);
    if (createStatus != DiagnosticCredentialMutationStatus.Succeeded)
    {
      await transaction.RollbackAsync(cancellationToken);
      return new DiagnosticCredentialMutation(createStatus, null);
    }

    await using var revoke = connection.CreateCommand();
    revoke.Transaction = transaction;
    revoke.CommandText =
        """
        UPDATE diagnostic_credentials
        SET revoked_at = $rotatedAt,
            revoked_by_github_user_id = $rotatedByGitHubUserId
        WHERE tenant_id = $tenantId
          AND credential_id = $credentialId
          AND revoked_at IS NULL
          AND expires_at > $rotatedAt;
        """;
    revoke.Parameters.AddWithValue("$tenantId", tenantId);
    revoke.Parameters.AddWithValue(
        "$credentialId",
        FormatGuid(credentialId));
    revoke.Parameters.AddWithValue(
        "$rotatedByGitHubUserId",
        rotatedByGitHubUserId);
    revoke.Parameters.AddWithValue(
        "$rotatedAt",
        FormatTimestamp(rotatedAt));
    if (await revoke.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      await transaction.RollbackAsync(cancellationToken);
      return new DiagnosticCredentialMutation(
          DiagnosticCredentialMutationStatus.Conflict,
          null);
    }
    await transaction.CommitAsync(cancellationToken);
    return new DiagnosticCredentialMutation(
        DiagnosticCredentialMutationStatus.Succeeded,
        replacement);
  }

  private static async Task<DiagnosticCredentialMutationStatus> InsertAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DiagnosticCredentialWrite write,
      CancellationToken cancellationToken)
  {
    var credential = write.Credential;
    if (!await TenantExistsAsync(
        connection,
        transaction,
        credential.TenantId,
        cancellationToken))
    {
      return DiagnosticCredentialMutationStatus.NotFound;
    }
    if (!await NodesBelongToTenantAsync(
        connection,
        transaction,
        credential.TenantId,
        credential.NodeIds,
        cancellationToken))
    {
      return DiagnosticCredentialMutationStatus.InvalidNode;
    }

    await using var insert = connection.CreateCommand();
    insert.Transaction = transaction;
    insert.CommandText =
        """
        INSERT INTO diagnostic_credentials (
            credential_id,
            tenant_id,
            label,
            token_hash,
            created_by_github_user_id,
            created_at,
            expires_at,
            revoked_at,
            revoked_by_github_user_id,
            rotated_from_credential_id,
            last_used_at,
            use_count)
        VALUES (
            $credentialId,
            $tenantId,
            $label,
            $tokenHash,
            $createdByGitHubUserId,
            $createdAt,
            $expiresAt,
            $revokedAt,
            $revokedByGitHubUserId,
            $rotatedFromCredentialId,
            $lastUsedAt,
            $useCount)
        ON CONFLICT DO NOTHING;
        """;
    AddCredentialParameters(insert, write);
    if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      return DiagnosticCredentialMutationStatus.Conflict;
    }
    await InsertRestrictionsAsync(
        connection,
        transaction,
        credential,
        cancellationToken);
    return DiagnosticCredentialMutationStatus.Succeeded;
  }

  private static async Task InsertRestrictionsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DiagnosticCredential credential,
      CancellationToken cancellationToken)
  {
    if (credential.NodeIds.Count > 0)
    {
      await using var nodes = connection.CreateCommand();
      nodes.Transaction = transaction;
      var values = new StringBuilder();
      for (var index = 0; index < credential.NodeIds.Count; index++)
      {
        if (index > 0)
        {
          values.Append(',');
        }
        values.Append($"($credentialId, $node{index})");
        nodes.Parameters.AddWithValue(
            $"$node{index}",
            FormatGuid(credential.NodeIds[index]));
      }
      nodes.CommandText =
          $"INSERT INTO diagnostic_credential_nodes (credential_id, node_id) VALUES {values};";
      nodes.Parameters.AddWithValue(
          "$credentialId",
          FormatGuid(credential.CredentialId));
      await nodes.ExecuteNonQueryAsync(cancellationToken);
    }
    if (credential.ProfileIds.Count > 0)
    {
      await using var profiles = connection.CreateCommand();
      profiles.Transaction = transaction;
      var values = new StringBuilder();
      for (var index = 0; index < credential.ProfileIds.Count; index++)
      {
        if (index > 0)
        {
          values.Append(',');
        }
        values.Append($"($credentialId, $profile{index})");
        profiles.Parameters.AddWithValue(
            $"$profile{index}",
            credential.ProfileIds[index]);
      }
      profiles.CommandText =
          $"INSERT INTO diagnostic_credential_profiles (credential_id, profile_id) VALUES {values};";
      profiles.Parameters.AddWithValue(
          "$credentialId",
          FormatGuid(credential.CredentialId));
      await profiles.ExecuteNonQueryAsync(cancellationToken);
    }
  }

  private static async Task<bool> TenantExistsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        "SELECT EXISTS(SELECT 1 FROM tenants WHERE tenant_id = $tenantId);";
    command.Parameters.AddWithValue("$tenantId", tenantId);
    return Convert.ToInt32(
        await command.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture) == 1;
  }

  private static async Task<bool> NodesBelongToTenantAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      IReadOnlyList<Guid> nodeIds,
      CancellationToken cancellationToken)
  {
    if (nodeIds.Count == 0)
    {
      return true;
    }
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    var parameters = new string[nodeIds.Count];
    for (var index = 0; index < nodeIds.Count; index++)
    {
      parameters[index] = $"$node{index}";
      command.Parameters.AddWithValue(
          parameters[index],
          FormatGuid(nodeIds[index]));
    }
    command.CommandText =
        $"""
        SELECT COUNT(*)
        FROM nodes
        WHERE tenant_id = $tenantId
          AND node_id IN ({string.Join(", ", parameters)});
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    return Convert.ToInt32(
        await command.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture) == nodeIds.Count;
  }

  private static async Task<DiagnosticCredential?> GetActiveOrNullAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid credentialId,
      DateTimeOffset at,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        CredentialProjection + "\n" +
        """
        WHERE c.tenant_id = $tenantId
          AND c.credential_id = $credentialId
          AND c.revoked_at IS NULL
          AND c.expires_at > $at;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$credentialId",
        FormatGuid(credentialId));
    command.Parameters.AddWithValue("$at", FormatTimestamp(at));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? ReadCredential(new SqliteRowReader(reader))
        : null;
  }

  private static async Task<bool> ExistsAsync(
      SqliteConnection connection,
      string tenantId,
      Guid credentialId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT EXISTS(
            SELECT 1
            FROM diagnostic_credentials
            WHERE tenant_id = $tenantId
              AND credential_id = $credentialId);
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$credentialId",
        FormatGuid(credentialId));
    return Convert.ToInt32(
        await command.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture) == 1;
  }

  private static void AddCredentialParameters(
      SqliteCommand command,
      DiagnosticCredentialWrite write)
  {
    var credential = write.Credential;
    command.Parameters.AddWithValue(
        "$credentialId",
        FormatGuid(credential.CredentialId));
    command.Parameters.AddWithValue("$tenantId", credential.TenantId);
    command.Parameters.AddWithValue("$label", credential.Label);
    command.Parameters.AddWithValue("$tokenHash", write.TokenHash);
    command.Parameters.AddWithValue(
        "$createdByGitHubUserId",
        credential.CreatedByGitHubUserId);
    command.Parameters.AddWithValue(
        "$createdAt",
        FormatTimestamp(credential.CreatedAt));
    command.Parameters.AddWithValue(
        "$expiresAt",
        FormatTimestamp(credential.ExpiresAt));
    command.Parameters.AddWithValue(
        "$revokedAt",
        credential.RevokedAt is null
            ? DBNull.Value
            : FormatTimestamp(credential.RevokedAt.Value));
    command.Parameters.AddWithValue(
        "$revokedByGitHubUserId",
        (object?)credential.RevokedByGitHubUserId ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$rotatedFromCredentialId",
        credential.RotatedFromCredentialId is null
            ? DBNull.Value
            : FormatGuid(credential.RotatedFromCredentialId.Value));
    command.Parameters.AddWithValue(
        "$lastUsedAt",
        credential.LastUsedAt is null
            ? DBNull.Value
            : FormatTimestamp(credential.LastUsedAt.Value));
    command.Parameters.AddWithValue("$useCount", credential.UseCount);
  }

  private static DiagnosticCredential ReadCredential(SqliteRowReader row) =>
      new(
          Guid.Parse(
              row.String("credential_id"),
              CultureInfo.InvariantCulture),
          row.String("tenant_id"),
          row.String("label"),
          row.String("created_by_github_user_id"),
          row.Time("created_at"),
          row.Time("expires_at"),
          row.OptionalTime("revoked_at"),
          row.OptionalString("revoked_by_github_user_id"),
          ParseOptionalGuid(row.OptionalString("rotated_from_credential_id")),
          row.OptionalTime("last_used_at"),
          row.Int64("use_count"),
          ParseGuids(row.String("node_ids")),
          ParseStrings(row.String("profile_ids")));

  private static IReadOnlyList<Guid> ParseGuids(string value) =>
      string.IsNullOrWhiteSpace(value)
          ? []
          : value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
              .Select(value => Guid.Parse(
                  value,
                  CultureInfo.InvariantCulture))
              .ToArray();

  private static IReadOnlyList<string> ParseStrings(string value) =>
      string.IsNullOrWhiteSpace(value)
          ? []
          : value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

  private static Guid? ParseOptionalGuid(string? value) =>
      string.IsNullOrWhiteSpace(value)
          ? null
          : Guid.Parse(
              value,
              CultureInfo.InvariantCulture);

  private static string FormatGuid(Guid value) =>
      value.ToString("D", CultureInfo.InvariantCulture);

  private static string FormatTimestamp(DateTimeOffset value) =>
      value.ToUniversalTime().ToString(
          "O",
          CultureInfo.InvariantCulture);

  private const string CredentialProjection =
      """
      SELECT
          c.credential_id,
          c.tenant_id,
          c.label,
          c.created_by_github_user_id,
          c.created_at,
          c.expires_at,
          c.revoked_at,
          c.revoked_by_github_user_id,
          c.rotated_from_credential_id,
          c.last_used_at,
          c.use_count,
          COALESCE((
              SELECT group_concat(n.node_id, ',')
              FROM diagnostic_credential_nodes AS n
              WHERE n.credential_id = c.credential_id
              ORDER BY n.node_id), '') AS node_ids,
          COALESCE((
              SELECT group_concat(p.profile_id, ',')
              FROM diagnostic_credential_profiles AS p
              WHERE p.credential_id = c.credential_id
              ORDER BY p.profile_id), '') AS profile_ids
      FROM diagnostic_credentials AS c
      """;
}
