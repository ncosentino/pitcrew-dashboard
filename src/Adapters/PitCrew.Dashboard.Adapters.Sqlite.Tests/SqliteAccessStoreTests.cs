using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteAccessStoreTests
{
  [Test]
  public async Task Tenant_Rename_Preserves_Identity_Membership_And_Restart_Name(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-access-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteAccessStore(connectionFactory);
      var now = DateTimeOffset.UtcNow;
      var owner = new DashboardUser(
          "1",
          "owner",
          "Owner",
          null);
      await store.EnsureTenantOwnerAsync(
          "tenant",
          "Original tenant",
          owner,
          now,
          cancellationToken);

      var renamed = await store.RenameTenantAsync(
          "tenant",
          "Renamed tenant",
          cancellationToken);
      await store.EnsureTenantOwnerAsync(
          "tenant",
          "Original tenant",
          owner,
          now.AddMinutes(1),
          cancellationToken);
      var session = await store.GetSessionAsync(
          owner,
          false,
          cancellationToken);
      var members = await store.GetMembersAsync(
          "tenant",
          cancellationToken);
      var missing = await store.RenameTenantAsync(
          "missing",
          "Missing tenant",
          cancellationToken);

      await Assert.That(renamed)
          .IsEqualTo(AccessMutationStatus.Succeeded);
      await Assert.That(missing)
          .IsEqualTo(AccessMutationStatus.NotFound);
      await Assert.That(session.Tenants).HasSingleItem();
      await Assert.That(session.Tenants[0].TenantId)
          .IsEqualTo("tenant");
      await Assert.That(session.Tenants[0].DisplayName)
          .IsEqualTo("Renamed tenant");
      await Assert.That(members).HasSingleItem();
      await Assert.That(members[0].Role)
          .IsEqualTo(TenantRole.Owner);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Membership_Mutations_Preserve_The_Final_Owner(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-access-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteAccessStore(connectionFactory);
      var now = DateTimeOffset.UtcNow;
      var owner = new DashboardUser(
          "1",
          "owner",
          "Owner",
          null);
      var viewer = new DashboardUser(
          "2",
          "viewer",
          "Viewer",
          null);
      await store.EnsureTenantOwnerAsync(
          "tenant",
          "Tenant",
          owner,
          now,
          cancellationToken);
      await store.UpsertUserAsync(
          viewer,
          now,
          cancellationToken);
      await store.SetMembershipAsync(
          "tenant",
          viewer.GitHubUserId,
          TenantRole.Viewer,
          owner.GitHubUserId,
          now,
          cancellationToken);

      var blocked = await store.RemoveMembershipAsync(
          "tenant",
          owner.GitHubUserId,
          cancellationToken);
      var promoted = await store.SetMembershipAsync(
          "tenant",
          viewer.GitHubUserId,
          TenantRole.Owner,
          owner.GitHubUserId,
          now,
          cancellationToken);
      var removed = await store.RemoveMembershipAsync(
          "tenant",
          owner.GitHubUserId,
          cancellationToken);
      var viewerSession = await store.GetSessionAsync(
          viewer,
          false,
          cancellationToken);

      await Assert.That(blocked)
          .IsEqualTo(AccessMutationStatus.LastOwner);
      await Assert.That(promoted)
          .IsEqualTo(AccessMutationStatus.Succeeded);
      await Assert.That(removed)
          .IsEqualTo(AccessMutationStatus.Succeeded);
      await Assert.That(viewerSession.Tenants).HasSingleItem();
      await Assert.That(viewerSession.Tenants[0].Role)
          .IsEqualTo(TenantRole.Owner);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }
}

internal static class DashboardTestCleanup
{
  public static void DeleteDatabase(string databasePath)
  {
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
