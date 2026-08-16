using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteSupportStoreTests
{
  [Test]
  public async Task Support_Identity_Is_Tenant_Isolated_And_Revocation_Blocks_Sessions(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-support-{Guid.NewGuid():N}.db");
    try
    {
      var factory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
      var accessStore = new SqliteAccessStore(factory);
      var supportStore = new SqliteSupportStore(factory);
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var owner = new DashboardUser("1", "owner", "Owner", null);
      await accessStore.EnsureTenantOwnerAsync("tenant-a", "Tenant A", owner, now, cancellationToken);
      await accessStore.EnsureTenantOwnerAsync("tenant-b", "Tenant B", owner, now, cancellationToken);
      var keys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.Parse(
          "11111111-1111-1111-1111-111111111111",
          CultureInfo.InvariantCulture);
      var identity = new SupportIdentity(
          "tenant-a",
          nodeId,
          "Support node",
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
          owner.GitHubUserId,
          now,
          null,
          null,
          null,
          null,
          1);
      var status = await supportStore.CreateIdentityAsync(
          new SupportIdentityWrite(
              identity,
              "transport-hash",
              "enrollment-hash",
              now.AddHours(1)),
          cancellationToken);
      var tenantA = await supportStore.GetIdentitiesAsync("tenant-a", cancellationToken);
      var tenantB = await supportStore.GetIdentitiesAsync("tenant-b", cancellationToken);

      await Assert.That(status).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(tenantA).HasSingleItem();
      await Assert.That(tenantB).IsEmpty();

      var session = CreateSession("tenant-a", nodeId, now.AddMinutes(2));
      var staleKeySession = await supportStore.CreateSessionAsync(
          session,
          "retired-signing-key",
          "retired-encryption-key",
          cancellationToken);
      var revoked = await supportStore.RevokeIdentityAsync(
          "tenant-a",
          nodeId,
          owner.GitHubUserId,
          now.AddMinutes(1),
          cancellationToken);
      var sessionStatus = await supportStore.CreateSessionAsync(
          session,
          identity.NodeSigningPublicKeySpki,
          identity.NodeEncryptionPublicKeySpki,
          cancellationToken);

      await Assert.That(staleKeySession)
          .IsEqualTo(SupportMutationStatus.NotFound);
      await Assert.That(revoked).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(sessionStatus).IsEqualTo(SupportMutationStatus.NotFound);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Support_Session_Cancel_And_Complete_Are_Tenant_Bound(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-support-{Guid.NewGuid():N}.db");
    try
    {
      var factory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
      var accessStore = new SqliteAccessStore(factory);
      var supportStore = new SqliteSupportStore(factory);
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var owner = new DashboardUser("1", "owner", "Owner", null);
      await accessStore.EnsureTenantOwnerAsync("tenant-a", "Tenant A", owner, now, cancellationToken);
      await accessStore.EnsureTenantOwnerAsync("tenant-b", "Tenant B", owner, now, cancellationToken);
      var nodeId = Guid.Parse(
          "11111111-1111-1111-1111-111111111111",
          CultureInfo.InvariantCulture);
      var keys = SupportKeyFactory.CreateNodeKeys();
      await supportStore.CreateIdentityAsync(
          new SupportIdentityWrite(
              new SupportIdentity(
                  "tenant-a",
                  nodeId,
                  "Support node",
                  keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
                  keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
                  owner.GitHubUserId,
                  now,
                  null,
                  null,
                  null,
                  null,
                  1),
              "transport-hash",
              "enrollment-hash",
              now.AddHours(1)),
          cancellationToken);
      var session = CreateSession("tenant-a", nodeId, now.AddMinutes(1));
      var created = await supportStore.CreateSessionAsync(
          session,
          keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
          cancellationToken);
      var crossTenant = await supportStore.GetSessionOrNullAsync(
          "tenant-b",
          session.SessionId,
          cancellationToken);
      var cancelWrongTenant = await supportStore.CancelSessionAsync(
          "tenant-b",
          session.SessionId,
          now.AddMinutes(2),
          cancellationToken);
      var completed = await supportStore.CompleteSessionAsync(
          "tenant-a",
          session.SessionId,
          JsonSerializer.Serialize(session.RequestEnvelope),
          "{\"verified\":[],\"unavailable\":[],\"hypotheses\":[]}",
          "# Report",
          JsonSerializer.Serialize(new SupportResultAttestation(
              keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
              "payload",
              "signature",
              SupportEnvelopeCryptography.SignatureAlgorithm)),
          now.AddMinutes(3),
          cancellationToken);
      var stored = await supportStore.GetSessionOrNullAsync(
          "tenant-a",
          session.SessionId,
          cancellationToken);

      await Assert.That(created).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(crossTenant).IsNull();
      await Assert.That(cancelWrongTenant).IsEqualTo(SupportMutationStatus.Conflict);
      await Assert.That(completed).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.Status).IsEqualTo(SupportDiagnosticSessionStatus.Completed);
      await Assert.That(stored.Markdown).IsEqualTo("# Report");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static SupportDiagnosticSession CreateSession(
      string tenantId,
      Guid nodeId,
      DateTimeOffset requestedAt)
  {
    var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
    var nodeKeys = SupportKeyFactory.CreateNodeKeys();
    using var signing = SupportKeyFactory.ImportEcdsaPrivateKey(
        dashboardKeys.AuthorizationSigning.PrivateKeyPkcs8Base64Url);
    using var nodeEncryption = SupportKeyFactory.ImportRsaPublicKey(
        nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
    var request = new SupportDiagnosticRequest(
        "support-plane-v1",
        tenantId,
        nodeId,
        Guid.NewGuid(),
        SupportCapability.DiagnosticsSnapshotV1,
        1,
        SupportDiagnosticModes.Full,
        null,
        "support-package",
        requestedAt,
        requestedAt.AddMinutes(10),
        "nonce-abcdefghijklmnopqrstuvwxyz0123456789");
    var requestPayload = SupportCanonicalJson.SerializeRequest(request);
    var envelope = SupportEnvelopeCryptography.Seal(
        requestPayload,
        nodeEncryption,
        signing,
        "dashboard",
        nodeId.ToString("N"));
    return new SupportDiagnosticSession(
        tenantId,
        request.SessionId,
        nodeId,
        SupportDiagnosticModes.Full,
        null,
        request.PackageId,
        SupportCapability.DiagnosticsSnapshotV1,
        Convert.ToHexString(SHA256.HashData(requestPayload)).ToLowerInvariant(),
        Convert.ToHexString(SHA256.HashData(SupportBase64Url.Decode(
            nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url))).ToLowerInvariant(),
        SupportDiagnosticSessionStatus.Queued,
        "1",
        requestedAt,
        request.ExpiresAt,
        envelope,
        null,
        null,
        null,
        null,
        null);
  }
}
