using System.Globalization;

using Microsoft.Data.Sqlite;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteAccessStore(
    SqliteConnectionFactory _connectionFactory) : IAccessStore
{
  public async Task UpsertUserAsync(
      DashboardUser user,
      DateTimeOffset observedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO dashboard_users (
            github_user_id,
            github_login,
            display_name,
            avatar_url,
            first_seen_at,
            last_seen_at)
        VALUES (
            $githubUserId,
            $githubLogin,
            $displayName,
            $avatarUrl,
            $observedAt,
            $observedAt)
        ON CONFLICT (github_user_id) DO UPDATE SET
            github_login = excluded.github_login,
            display_name = excluded.display_name,
            avatar_url = excluded.avatar_url,
            last_seen_at = excluded.last_seen_at
        WHERE dashboard_users.github_login <> excluded.github_login
           OR dashboard_users.display_name <> excluded.display_name
           OR dashboard_users.avatar_url IS NOT excluded.avatar_url
           OR dashboard_users.last_seen_at < $refreshBefore;
        """;
    AddUserParameters(command, user);
    command.Parameters.AddWithValue(
        "$observedAt",
        FormatTimestamp(observedAt));
    command.Parameters.AddWithValue(
        "$refreshBefore",
        FormatTimestamp(observedAt.AddMinutes(-15)));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<DashboardSession> GetSessionAsync(
      DashboardUser user,
      bool isSystemAdministrator,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    if (isSystemAdministrator)
    {
      command.CommandText =
          """
          SELECT
              t.tenant_id,
              t.display_name,
              COALESCE(m.role, 'owner')
          FROM tenants AS t
          LEFT JOIN tenant_memberships AS m
              ON m.tenant_id = t.tenant_id
              AND m.github_user_id = $githubUserId
          ORDER BY t.display_name, t.tenant_id;
          """;
    }
    else
    {
      command.CommandText =
          """
          SELECT
              t.tenant_id,
              t.display_name,
              m.role
          FROM tenant_memberships AS m
          INNER JOIN tenants AS t
              ON t.tenant_id = m.tenant_id
          WHERE m.github_user_id = $githubUserId
          ORDER BY t.display_name, t.tenant_id;
          """;
    }
    command.Parameters.AddWithValue(
        "$githubUserId",
        user.GitHubUserId);

    var tenants = new List<TenantAccess>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      tenants.Add(new TenantAccess(
          reader.GetString(0),
          reader.GetString(1),
          ParseRole(reader.GetString(2))));
    }
    return new DashboardSession(
        user,
        isSystemAdministrator,
        tenants);
  }

  public async Task<TenantRole?> GetRoleOrNullAsync(
      string tenantId,
      string githubUserId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT role
        FROM tenant_memberships
        WHERE tenant_id = $tenantId
          AND github_user_id = $githubUserId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$githubUserId",
        githubUserId);
    var value = Convert.ToString(
        await command.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    return string.IsNullOrWhiteSpace(value)
        ? null
        : ParseRole(value);
  }

  public async Task<AccessMutationStatus> CreateTenantAsync(
      string tenantId,
      string displayName,
      string ownerGitHubUserId,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var tenantCommand = connection.CreateCommand();
    tenantCommand.Transaction = transaction;
    tenantCommand.CommandText =
        """
        INSERT INTO tenants (
            tenant_id,
            display_name,
            created_at)
        VALUES (
            $tenantId,
            $displayName,
            $createdAt)
        ON CONFLICT (tenant_id) DO NOTHING;
        """;
    tenantCommand.Parameters.AddWithValue("$tenantId", tenantId);
    tenantCommand.Parameters.AddWithValue(
        "$displayName",
        displayName);
    tenantCommand.Parameters.AddWithValue(
        "$createdAt",
        FormatTimestamp(createdAt));
    if (await tenantCommand.ExecuteNonQueryAsync(cancellationToken) == 0)
    {
      await transaction.RollbackAsync(cancellationToken);
      return AccessMutationStatus.Conflict;
    }

    await using var membershipCommand = connection.CreateCommand();
    membershipCommand.Transaction = transaction;
    membershipCommand.CommandText =
        """
        INSERT INTO tenant_memberships (
            tenant_id,
            github_user_id,
            role,
            created_at,
            created_by_github_user_id)
        VALUES (
            $tenantId,
            $githubUserId,
            'owner',
            $createdAt,
            $githubUserId);
        """;
    membershipCommand.Parameters.AddWithValue(
        "$tenantId",
        tenantId);
    membershipCommand.Parameters.AddWithValue(
        "$githubUserId",
        ownerGitHubUserId);
    membershipCommand.Parameters.AddWithValue(
        "$createdAt",
        FormatTimestamp(createdAt));
    await membershipCommand.ExecuteNonQueryAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return AccessMutationStatus.Succeeded;
  }

  public async Task<AccessMutationStatus> RenameTenantAsync(
      string tenantId,
      string displayName,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE tenants
        SET display_name = $displayName
        WHERE tenant_id = $tenantId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$displayName",
        displayName);
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? AccessMutationStatus.Succeeded
        : AccessMutationStatus.NotFound;
  }

  public async Task EnsureTenantOwnerAsync(
      string tenantId,
      string displayName,
      DashboardUser owner,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    await using (var userCommand = connection.CreateCommand())
    {
      userCommand.Transaction = transaction;
      userCommand.CommandText =
          """
          INSERT INTO dashboard_users (
              github_user_id,
              github_login,
              display_name,
              avatar_url,
              first_seen_at,
              last_seen_at)
          VALUES (
              $githubUserId,
              $githubLogin,
              $displayName,
              $avatarUrl,
              $createdAt,
              $createdAt)
          ON CONFLICT (github_user_id) DO UPDATE SET
              github_login = excluded.github_login,
              display_name = excluded.display_name,
              avatar_url = excluded.avatar_url,
              last_seen_at = excluded.last_seen_at;
          """;
      AddUserParameters(userCommand, owner);
      userCommand.Parameters.AddWithValue(
          "$createdAt",
          FormatTimestamp(createdAt));
      await userCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    await using (var tenantCommand = connection.CreateCommand())
    {
      tenantCommand.Transaction = transaction;
      tenantCommand.CommandText =
          """
          INSERT INTO tenants (
              tenant_id,
              display_name,
              created_at)
          VALUES (
              $tenantId,
              $displayName,
              $createdAt)
          ON CONFLICT (tenant_id) DO NOTHING;
          """;
      tenantCommand.Parameters.AddWithValue("$tenantId", tenantId);
      tenantCommand.Parameters.AddWithValue(
          "$displayName",
          displayName);
      tenantCommand.Parameters.AddWithValue(
          "$createdAt",
          FormatTimestamp(createdAt));
      await tenantCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    await using (var membershipCommand = connection.CreateCommand())
    {
      membershipCommand.Transaction = transaction;
      membershipCommand.CommandText =
          """
          INSERT INTO tenant_memberships (
              tenant_id,
              github_user_id,
              role,
              created_at,
              created_by_github_user_id)
          VALUES (
              $tenantId,
              $githubUserId,
              'owner',
              $createdAt,
              $githubUserId)
          ON CONFLICT (tenant_id, github_user_id) DO UPDATE SET
              role = 'owner';
          """;
      membershipCommand.Parameters.AddWithValue(
          "$tenantId",
          tenantId);
      membershipCommand.Parameters.AddWithValue(
          "$githubUserId",
          owner.GitHubUserId);
      membershipCommand.Parameters.AddWithValue(
          "$createdAt",
          FormatTimestamp(createdAt));
      await membershipCommand.ExecuteNonQueryAsync(
          cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<TenantMember>> GetMembersAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            u.github_user_id,
            u.github_login,
            u.display_name,
            u.avatar_url,
            m.role,
            m.created_at
        FROM tenant_memberships AS m
        INNER JOIN dashboard_users AS u
            ON u.github_user_id = m.github_user_id
        WHERE m.tenant_id = $tenantId
        ORDER BY u.github_login COLLATE NOCASE;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);

    var members = new List<TenantMember>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      members.Add(new TenantMember(
          ReadUser(reader),
          ParseRole(reader.GetString(4)),
          ParseTimestamp(reader.GetString(5))));
    }
    return members;
  }

  public async Task<IReadOnlyList<DashboardUser>> GetAvailableUsersAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            u.github_user_id,
            u.github_login,
            u.display_name,
            u.avatar_url
        FROM dashboard_users AS u
        LEFT JOIN tenant_memberships AS m
            ON m.tenant_id = $tenantId
            AND m.github_user_id = u.github_user_id
        WHERE m.github_user_id IS NULL
        ORDER BY u.github_login COLLATE NOCASE;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);

    var users = new List<DashboardUser>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      users.Add(ReadUser(reader));
    }
    return users;
  }

  public async Task<AccessMutationStatus> SetMembershipAsync(
      string tenantId,
      string githubUserId,
      TenantRole role,
      string createdByGitHubUserId,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var state = await ReadMembershipMutationStateAsync(
        connection,
        transaction,
        tenantId,
        githubUserId,
        cancellationToken);
    if (!state.TenantExists || !state.UserExists)
    {
      await transaction.RollbackAsync(cancellationToken);
      return AccessMutationStatus.NotFound;
    }
    if (state.CurrentRole == TenantRole.Owner &&
        role != TenantRole.Owner &&
        state.OwnerCount <= 1)
    {
      await transaction.RollbackAsync(cancellationToken);
      return AccessMutationStatus.LastOwner;
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO tenant_memberships (
            tenant_id,
            github_user_id,
            role,
            created_at,
            created_by_github_user_id)
        VALUES (
            $tenantId,
            $githubUserId,
            $role,
            $createdAt,
            $createdByGitHubUserId)
        ON CONFLICT (tenant_id, github_user_id) DO UPDATE SET
            role = excluded.role;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$githubUserId",
        githubUserId);
    command.Parameters.AddWithValue("$role", FormatRole(role));
    command.Parameters.AddWithValue(
        "$createdAt",
        FormatTimestamp(createdAt));
    command.Parameters.AddWithValue(
        "$createdByGitHubUserId",
        createdByGitHubUserId);
    await command.ExecuteNonQueryAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return AccessMutationStatus.Succeeded;
  }

  public async Task<AccessMutationStatus> RemoveMembershipAsync(
      string tenantId,
      string githubUserId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var state = await ReadMembershipMutationStateAsync(
        connection,
        transaction,
        tenantId,
        githubUserId,
        cancellationToken);
    if (!state.TenantExists ||
        !state.UserExists ||
        state.CurrentRole is null)
    {
      await transaction.RollbackAsync(cancellationToken);
      return AccessMutationStatus.NotFound;
    }
    if (state.CurrentRole == TenantRole.Owner &&
        state.OwnerCount <= 1)
    {
      await transaction.RollbackAsync(cancellationToken);
      return AccessMutationStatus.LastOwner;
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM tenant_memberships
        WHERE tenant_id = $tenantId
          AND github_user_id = $githubUserId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$githubUserId",
        githubUserId);
    await command.ExecuteNonQueryAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return AccessMutationStatus.Succeeded;
  }

  private static async Task<MembershipMutationState>
      ReadMembershipMutationStateAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          string tenantId,
          string githubUserId,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            EXISTS (
                SELECT 1
                FROM tenants
                WHERE tenant_id = $tenantId),
            EXISTS (
                SELECT 1
                FROM dashboard_users
                WHERE github_user_id = $githubUserId),
            (
                SELECT role
                FROM tenant_memberships
                WHERE tenant_id = $tenantId
                  AND github_user_id = $githubUserId),
            (
                SELECT COUNT(*)
                FROM tenant_memberships
                WHERE tenant_id = $tenantId
                  AND role = 'owner');
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$githubUserId",
        githubUserId);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    await reader.ReadAsync(cancellationToken);
    return new MembershipMutationState(
        reader.GetBoolean(0),
        reader.GetBoolean(1),
        await reader.IsDBNullAsync(2, cancellationToken)
            ? null
            : ParseRole(reader.GetString(2)),
        reader.GetInt32(3));
  }

  private static void AddUserParameters(
      SqliteCommand command,
      DashboardUser user)
  {
    command.Parameters.AddWithValue(
        "$githubUserId",
        user.GitHubUserId);
    command.Parameters.AddWithValue(
        "$githubLogin",
        user.GitHubLogin);
    command.Parameters.AddWithValue(
        "$displayName",
        user.DisplayName);
    command.Parameters.AddWithValue(
        "$avatarUrl",
        (object?)user.AvatarUrl ?? DBNull.Value);
  }

  private static DashboardUser ReadUser(SqliteDataReader reader) =>
      new(
          reader.GetString(0),
          reader.GetString(1),
          reader.GetString(2),
          reader.IsDBNull(3)
              ? null
              : reader.GetString(3));

  private static string FormatRole(TenantRole role) =>
      role switch
      {
        TenantRole.Viewer => "viewer",
        TenantRole.Administrator => "administrator",
        TenantRole.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
      };

  private static TenantRole ParseRole(string role) =>
      role switch
      {
        "viewer" => TenantRole.Viewer,
        "administrator" => TenantRole.Administrator,
        "owner" => TenantRole.Owner,
        _ => throw new InvalidOperationException(
            $"Stored tenant role '{role}' is invalid."),
      };

  private static string FormatTimestamp(DateTimeOffset value) =>
      value.ToString("O", CultureInfo.InvariantCulture);

  private static DateTimeOffset ParseTimestamp(string value) =>
      DateTimeOffset.Parse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

  private sealed record MembershipMutationState(
      bool TenantExists,
      bool UserExists,
      TenantRole? CurrentRole,
      int OwnerCount);
}
