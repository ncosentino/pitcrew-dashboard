using System.Globalization;
using System.Security.Cryptography;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteSupportIdentityLifecycleTests
{
  [Test]
  public async Task Activity_Projection_Is_Monotonic_And_Tenant_Bound(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var context = await CreateContextAsync(databasePath, cancellationToken);
      var nodeA = Guid.NewGuid();
      var nodeB = Guid.NewGuid();
      await context.Store.CreateIdentityAsync(
          CreateIdentityWrite(
              CreateEnrollment(
                  "tenant-a",
                  "enrollment-hash-activity-a",
                  context.Now,
                  context.Owner.GitHubUserId),
              nodeA,
              SupportKeyFactory.CreateNodeKeys(),
              "transport-hash-activity-a"),
          cancellationToken);
      await context.Store.CreateIdentityAsync(
          CreateIdentityWrite(
              CreateEnrollment(
                  "tenant-b",
                  "enrollment-hash-activity-b",
                  context.Now,
                  context.Owner.GitHubUserId),
              nodeB,
              SupportKeyFactory.CreateNodeKeys(),
              "transport-hash-activity-b"),
          cancellationToken);
      var pollAt = context.Now.AddMinutes(2);
      var resultAt = context.Now.AddMinutes(3);

      await context.Store.UpdateIdentityActivityAsync(
          "tenant-a",
          [
            new SupportIdentityActivity(nodeA, pollAt, resultAt),
            new SupportIdentityActivity(
                nodeB,
                pollAt.AddMinutes(1),
                resultAt.AddMinutes(1)),
          ],
          cancellationToken);
      await context.Store.UpdateIdentityActivityAsync(
          "tenant-a",
          [new SupportIdentityActivity(nodeA, pollAt.AddMinutes(-1), null)],
          cancellationToken);

      var identityA = await context.Store.GetIdentityOrNullAsync(
          "tenant-a",
          nodeA,
          cancellationToken);
      var identityB = await context.Store.GetIdentityOrNullAsync(
          "tenant-b",
          nodeB,
          cancellationToken);

      await Assert.That(identityA).IsNotNull();
      await Assert.That(identityA!.LastPollAt).IsEqualTo(pollAt);
      await Assert.That(identityA.LastResultAt).IsEqualTo(resultAt);
      await Assert.That(identityB).IsNotNull();
      await Assert.That(identityB!.LastPollAt).IsNull();
      await Assert.That(identityB.LastResultAt).IsNull();
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Enrollment_Is_Tenant_Bound_Expires_And_Cannot_Replay(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var context = await CreateContextAsync(databasePath, cancellationToken);
      var keys = SupportKeyFactory.CreateNodeKeys();
      var enrollment = CreateEnrollment(
          "tenant-a",
          "enrollment-hash-a",
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateEnrollmentAsync(enrollment, cancellationToken);

      var crossTenant = await context.Store.GetEnrollmentOrNullAsync(
          "tenant-b",
          enrollment.EnrollmentCodeHash,
          cancellationToken);
      var expired = await context.Store.CompleteEnrollmentAsync(
          enrollment.EnrollmentId,
          Guid.NewGuid(),
          CreateIdentityWrite(
              enrollment,
              Guid.NewGuid(),
              keys,
              "transport-hash-expired"),
          CreateEnvelope(),
          enrollment.ExpiresAt.AddSeconds(1),
          enrollment.ExpiresAt.AddHours(1),
          Guid.NewGuid(),
          cancellationToken);

      var replayEnrollment = CreateEnrollment(
          "tenant-a",
          "enrollment-hash-b",
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateEnrollmentAsync(
          replayEnrollment,
          cancellationToken);
      var write = CreateIdentityWrite(
          replayEnrollment,
          Guid.NewGuid(),
          keys,
          "transport-hash-active");
      var cleanupLeaseId = Guid.NewGuid();
      await context.Store.QueueRelayCleanupAsync(
          write.Identity.NodeId,
          context.Now,
          cleanupLeaseId,
          context.Now.AddMinutes(1),
          cancellationToken);
      var completed = await context.Store.CompleteEnrollmentAsync(
          replayEnrollment.EnrollmentId,
          Guid.NewGuid(),
          write,
          CreateEnvelope(),
          context.Now,
          context.Now.AddHours(1),
          cleanupLeaseId,
          cancellationToken);
      var cleanupAfterCommit = await context.Store.ClaimRelayCleanupAsync(
          context.Now.AddMinutes(2),
          Guid.NewGuid(),
          context.Now.AddMinutes(4),
          limit: 8,
          cancellationToken);
      var replayed = await context.Store.CompleteEnrollmentAsync(
          replayEnrollment.EnrollmentId,
          Guid.NewGuid(),
          write with
          {
            Identity = write.Identity with { NodeId = Guid.NewGuid() },
          },
          CreateEnvelope(),
          context.Now,
          context.Now.AddHours(1),
          Guid.NewGuid(),
          cancellationToken);
      var recoverable = await context.Store.GetEnrollmentOrNullAsync(
          replayEnrollment.TenantId,
          replayEnrollment.EnrollmentCodeHash,
          cancellationToken);
      await context.Store.PurgeExpiredEnrollmentsAsync(
          context.Now.AddHours(2),
          limit: 64,
          cancellationToken);
      var purged = await context.Store.GetEnrollmentOrNullAsync(
          replayEnrollment.TenantId,
          replayEnrollment.EnrollmentCodeHash,
          cancellationToken);

      await Assert.That(crossTenant).IsNull();
      await Assert.That(expired).IsEqualTo(SupportMutationStatus.Invalid);
      await Assert.That(completed).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(cleanupAfterCommit).IsEmpty();
      await Assert.That(replayed).IsEqualTo(SupportMutationStatus.Conflict);
      await Assert.That(recoverable).IsNotNull();
      await Assert.That(recoverable!.RecoveryExpiresAt)
          .IsEqualTo(context.Now.AddHours(1));
      await Assert.That(purged).IsNull();
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Duplicate_Node_Key_Pair_Is_Rejected(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var context = await CreateContextAsync(databasePath, cancellationToken);
      var keys = SupportKeyFactory.CreateNodeKeys();
      var firstEnrollment = CreateEnrollment(
          "tenant-a",
          "enrollment-hash-first",
          context.Now,
          context.Owner.GitHubUserId);
      var secondEnrollment = CreateEnrollment(
          "tenant-a",
          "enrollment-hash-second",
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateEnrollmentAsync(
          firstEnrollment,
          cancellationToken);
      await context.Store.CreateEnrollmentAsync(
          secondEnrollment,
          cancellationToken);
      var firstWrite = CreateIdentityWrite(
          firstEnrollment,
          Guid.NewGuid(),
          keys,
          "transport-hash-first");
      var firstCleanupLeaseId = Guid.NewGuid();
      await context.Store.QueueRelayCleanupAsync(
          firstWrite.Identity.NodeId,
          context.Now,
          firstCleanupLeaseId,
          context.Now.AddMinutes(1),
          cancellationToken);
      var first = await context.Store.CompleteEnrollmentAsync(
          firstEnrollment.EnrollmentId,
          Guid.NewGuid(),
          firstWrite,
          CreateEnvelope(),
          context.Now,
          context.Now.AddHours(1),
          firstCleanupLeaseId,
          cancellationToken);
      var duplicate = await context.Store.CompleteEnrollmentAsync(
          secondEnrollment.EnrollmentId,
          Guid.NewGuid(),
          CreateIdentityWrite(
              secondEnrollment,
              Guid.NewGuid(),
              keys,
              "transport-hash-second"),
          CreateEnvelope(),
          context.Now,
          context.Now.AddHours(1),
          Guid.NewGuid(),
          cancellationToken);

      await Assert.That(first).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(duplicate).IsEqualTo(SupportMutationStatus.Conflict);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Rotation_Is_Atomic_Idempotent_And_Rejects_Old_Credential(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var context = await CreateContextAsync(databasePath, cancellationToken);
      var originalKeys = SupportKeyFactory.CreateNodeKeys();
      var replacementKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      await context.Store.CreateIdentityAsync(
          new SupportIdentityWrite(
              new SupportIdentity(
                  "tenant-a",
                  nodeId,
                  "Support node",
                  originalKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
                  originalKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
                  context.Owner.GitHubUserId,
                  context.Now,
                  null,
                  null,
                  null,
                  null,
                  1),
              "transport-hash-old",
              "enrollment-hash",
              context.Now.AddHours(1)),
          cancellationToken);
      var rotation = new SupportIdentityRotation(
          Guid.NewGuid(),
          "tenant-a",
          nodeId,
          "transport-hash-old",
          "transport-hash-new",
          replacementKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          replacementKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);

      var authorized = await context.Store.GetIdentityRotationStatusAsync(
          rotation,
          cancellationToken);
      var prepared = await context.Store.PrepareIdentityRotationAsync(
          rotation,
          context.Now,
          cancellationToken);
      var prepareRetry = await context.Store.PrepareIdentityRotationAsync(
          rotation,
          context.Now,
          cancellationToken);
      var preparedStatus = await context.Store.GetIdentityRotationStatusAsync(
          rotation,
          cancellationToken);
      var blockedSession = await context.Store.CreateSessionAsync(
          CreateSession("tenant-a", nodeId, context.Now),
          originalKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          originalKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
          cancellationToken);
      var promoted = await context.Store.PromoteIdentityRotationAsync(
          rotation,
          context.Now.AddMinutes(1),
          cancellationToken);
      var promotedStatus = await context.Store.GetIdentityRotationStatusAsync(
          rotation,
          cancellationToken);
      var finalized = await context.Store.FinalizeIdentityRotationAsync(
          rotation,
          context.Now.AddMinutes(2),
          cancellationToken);
      var finalStatus = await context.Store.GetIdentityRotationStatusAsync(
          rotation,
          cancellationToken);
      var acceptedSession = await context.Store.CreateSessionAsync(
          CreateSession("tenant-a", nodeId, context.Now.AddMinutes(3)),
          replacementKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          replacementKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
          cancellationToken);
      var retiredKeySession = await context.Store.CreateSessionAsync(
          CreateSession("tenant-a", nodeId, context.Now.AddMinutes(4)),
          originalKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
          originalKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
          cancellationToken);
      var oldCredential = await context.Store.GetIdentityRotationStatusAsync(
          rotation with
          {
            RotationId = Guid.NewGuid(),
            ExpectedTransportCredentialHash = "transport-hash-old",
            ReplacementTransportCredentialHash = "transport-hash-other",
          },
          cancellationToken);
      var stored = await context.Store.GetIdentityOrNullAsync(
          "tenant-a",
          nodeId,
          cancellationToken);

      await Assert.That(authorized)
          .IsEqualTo(SupportIdentityRotationStatus.Authorized);
      await Assert.That(prepared).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(prepareRetry).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(preparedStatus)
          .IsEqualTo(SupportIdentityRotationStatus.Prepared);
      await Assert.That(blockedSession)
          .IsEqualTo(SupportMutationStatus.NotFound);
      await Assert.That(promoted).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(promotedStatus)
          .IsEqualTo(SupportIdentityRotationStatus.DashboardPromoted);
      await Assert.That(finalized).IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(finalStatus)
          .IsEqualTo(SupportIdentityRotationStatus.Finalized);
      await Assert.That(acceptedSession)
          .IsEqualTo(SupportMutationStatus.Succeeded);
      await Assert.That(retiredKeySession)
          .IsEqualTo(SupportMutationStatus.NotFound);
      await Assert.That(oldCredential)
          .IsEqualTo(SupportIdentityRotationStatus.Forbidden);
      await Assert.That(stored).IsNotNull();
      await Assert.That(stored!.NodeSigningPublicKeySpki)
          .IsEqualTo(
              replacementKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Migration_22_Preserves_Legacy_Duplicate_Key_Pairs(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var factory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await SqliteMigrationTestDatabase.ApplyThroughAsync(
          factory,
          maximumVersion: 21,
          cancellationToken);
      var accessStore = new SqliteAccessStore(factory);
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var owner = new DashboardUser("1", "owner", "Owner", null);
      await accessStore.EnsureTenantOwnerAsync(
          "tenant-a",
          "Tenant A",
          owner,
          now,
          cancellationToken);
      await accessStore.EnsureTenantOwnerAsync(
          "tenant-b",
          "Tenant B",
          owner,
          now,
          cancellationToken);
      var keys = SupportKeyFactory.CreateNodeKeys();
      await using (var connection = await factory.OpenAsync(cancellationToken))
      await using (var command = connection.CreateCommand())
      {
        command.CommandText =
            """
            INSERT INTO support_nodes (
                node_id,
                tenant_id,
                display_name,
                node_signing_public_key_spki,
                node_encryption_public_key_spki,
                transport_credential_hash,
                enrollment_code_hash,
                enrollment_expires_at,
                enrollment_consumed_at,
                created_by_github_user_id,
                created_at,
                capability_version)
            VALUES
                ($firstNodeId, 'tenant-a', 'Legacy A', $signingKey,
                 $encryptionKey, 'legacy-hash-a', 'legacy-code-a',
                 $expiresAt, $createdAt, $ownerId, $createdAt, 1),
                ($secondNodeId, 'tenant-b', 'Legacy B', $signingKey,
                 $encryptionKey, 'legacy-hash-b', 'legacy-code-b',
                 $expiresAt, $createdAt, $ownerId, $createdAt, 1);
            """;
        command.Parameters.AddWithValue(
            "$firstNodeId",
            Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue(
            "$secondNodeId",
            Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue(
            "$signingKey",
            keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url);
        command.Parameters.AddWithValue(
            "$encryptionKey",
            keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
        command.Parameters.AddWithValue("$expiresAt", Format(now.AddHours(1)));
        command.Parameters.AddWithValue("$createdAt", Format(now));
        command.Parameters.AddWithValue("$ownerId", owner.GitHubUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
      }

      await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
      var store = new SqliteSupportStore(factory);
      var tenantA = await store.GetIdentitiesAsync(
          "tenant-a",
          cancellationToken);
      var tenantB = await store.GetIdentitiesAsync(
          "tenant-b",
          cancellationToken);
      var duplicate = await store.CreateIdentityAsync(
          new SupportIdentityWrite(
              new SupportIdentity(
                  "tenant-a",
                  Guid.NewGuid(),
                  "New duplicate",
                  keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
                  keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
                  owner.GitHubUserId,
                  now,
                  null,
                  null,
                  null,
                  null,
                  1),
              "new-hash",
              "new-code",
              now.AddHours(1)),
          cancellationToken);

      await Assert.That(tenantA).Count().IsEqualTo(1);
      await Assert.That(tenantB).Count().IsEqualTo(1);
      await Assert.That(duplicate).IsEqualTo(SupportMutationStatus.Conflict);
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Relay_Cleanup_Is_Durable_And_Retriable(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var context = await CreateContextAsync(databasePath, cancellationToken);
      var nodeId = Guid.NewGuid();
      var enrollmentLeaseId = Guid.NewGuid();
      await context.Store.QueueRelayCleanupAsync(
          nodeId,
          context.Now,
          enrollmentLeaseId,
          context.Now.AddMinutes(1),
          cancellationToken);
      var protectedCleanup = await context.Store.ClaimRelayCleanupAsync(
          context.Now,
          Guid.NewGuid(),
          context.Now.AddMinutes(2),
          limit: 8,
          cancellationToken);
      var firstMaintenanceLeaseId = Guid.NewGuid();
      var queued = await context.Store.ClaimRelayCleanupAsync(
          context.Now.AddMinutes(1),
          firstMaintenanceLeaseId,
          context.Now.AddMinutes(3),
          limit: 8,
          cancellationToken);
      var concurrentlyClaimed = await context.Store.ClaimRelayCleanupAsync(
          context.Now.AddMinutes(1),
          Guid.NewGuid(),
          context.Now.AddMinutes(3),
          limit: 8,
          cancellationToken);
      var staleDefer = await context.Store.DeferRelayCleanupAsync(
          nodeId,
          enrollmentLeaseId,
          context.Now.AddMinutes(3),
          cancellationToken);
      var deferred = await context.Store.DeferRelayCleanupAsync(
          nodeId,
          firstMaintenanceLeaseId,
          context.Now.AddMinutes(3),
          cancellationToken);
      var beforeBackoff = await context.Store.ClaimRelayCleanupAsync(
          context.Now.AddMinutes(2),
          Guid.NewGuid(),
          context.Now.AddMinutes(4),
          limit: 8,
          cancellationToken);
      var secondMaintenanceLeaseId = Guid.NewGuid();
      var retriable = await context.Store.ClaimRelayCleanupAsync(
          context.Now.AddMinutes(3),
          secondMaintenanceLeaseId,
          context.Now.AddMinutes(5),
          limit: 8,
          cancellationToken);
      var staleComplete = await context.Store.CompleteRelayCleanupAsync(
          nodeId,
          firstMaintenanceLeaseId,
          cancellationToken);
      var completed = await context.Store.CompleteRelayCleanupAsync(
          nodeId,
          secondMaintenanceLeaseId,
          cancellationToken);
      var afterCompletion = await context.Store.ClaimRelayCleanupAsync(
          context.Now.AddMinutes(6),
          Guid.NewGuid(),
          context.Now.AddMinutes(8),
          limit: 8,
          cancellationToken);

      await Assert.That(protectedCleanup).IsEmpty();
      await Assert.That(queued).Count().IsEqualTo(1);
      await Assert.That(queued[0].LeaseId)
          .IsEqualTo(firstMaintenanceLeaseId);
      await Assert.That(concurrentlyClaimed).IsEmpty();
      await Assert.That(staleDefer).IsFalse();
      await Assert.That(deferred).IsTrue();
      await Assert.That(beforeBackoff).IsEmpty();
      await Assert.That(retriable).Count().IsEqualTo(1);
      await Assert.That(retriable[0].AttemptCount).IsEqualTo(2);
      await Assert.That(retriable[0].LastAttemptAt)
          .IsEqualTo(context.Now.AddMinutes(3));
      await Assert.That(staleComplete).IsFalse();
      await Assert.That(completed).IsTrue();
      await Assert.That(afterCompletion).IsEmpty();
    }
    finally
    {
      DeleteDatabase(databasePath);
    }
  }

  private static async Task<TestContext> CreateContextAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var factory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
    var accessStore = new SqliteAccessStore(factory);
    var now = DateTimeOffset.Parse(
        "2026-08-01T00:00:00+00:00",
        CultureInfo.InvariantCulture);
    var owner = new DashboardUser("1", "owner", "Owner", null);
    await accessStore.EnsureTenantOwnerAsync(
        "tenant-a",
        "Tenant A",
        owner,
        now,
        cancellationToken);
    await accessStore.EnsureTenantOwnerAsync(
        "tenant-b",
        "Tenant B",
        owner,
        now,
        cancellationToken);
    return new TestContext(new SqliteSupportStore(factory), owner, now);
  }

  private static SupportEnrollment CreateEnrollment(
      string tenantId,
      string enrollmentCodeHash,
      DateTimeOffset now,
      string actorId) =>
      new(
          Guid.NewGuid(),
          tenantId,
          "Support node",
          enrollmentCodeHash,
          actorId,
          now,
          now.AddHours(1),
          null,
          null,
          null,
          null,
          null);

  private static SupportEnvelope CreateEnvelope() =>
      new(
          SupportEnvelopeCryptography.EnvelopeVersion,
          SupportEnvelopeCryptography.ContentEncryptionAlgorithm,
          SupportEnvelopeCryptography.KeyWrapAlgorithm,
          SupportEnvelopeCryptography.SignatureAlgorithm,
          "sender",
          "recipient",
          "wrapped",
          "nonce",
          "ciphertext",
          "tag",
          "signature");

  private static SupportIdentityWrite CreateIdentityWrite(
      SupportEnrollment enrollment,
      Guid nodeId,
      SupportNodeKeySet keys,
      string transportCredentialHash) =>
      new(
          new SupportIdentity(
              enrollment.TenantId,
              nodeId,
              enrollment.DisplayName,
              keys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
              keys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url,
              enrollment.CreatedByGitHubUserId,
              enrollment.CreatedAt,
              null,
              null,
              null,
              null,
              1),
          transportCredentialHash,
          enrollment.EnrollmentCodeHash,
          enrollment.ExpiresAt);

  private static SupportDiagnosticSession CreateSession(
      string tenantId,
      Guid nodeId,
      DateTimeOffset requestedAt)
  {
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
    return new SupportDiagnosticSession(
        tenantId,
        request.SessionId,
        nodeId,
        SupportDiagnosticModes.Full,
        null,
        request.PackageId,
        SupportCapability.DiagnosticsSnapshotV1,
        Convert.ToHexString(SHA256.HashData(requestPayload))
            .ToLowerInvariant(),
        new string('a', 64),
        SupportDiagnosticSessionStatus.Queued,
        "1",
        requestedAt,
        request.ExpiresAt,
        CreateEnvelope(),
        null,
        null,
        null,
        null,
        null);
  }

  private static string CreateDatabasePath() =>
      Path.Combine(
          AppContext.BaseDirectory,
          $"support-identity-{Guid.NewGuid():N}.db");

  private static string Format(DateTimeOffset value) =>
      value.ToString("O", CultureInfo.InvariantCulture);

  private static void DeleteDatabase(string databasePath)
  {
    SqliteConnection.ClearAllPools();
    DashboardTestCleanup.DeleteDatabase(databasePath);
  }

  private sealed record TestContext(
      SqliteSupportStore Store,
      DashboardUser Owner,
      DateTimeOffset Now);
}
