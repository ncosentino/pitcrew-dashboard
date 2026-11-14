using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Support.Agent.App;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportNodeIdentityStoreTests
{
  [Test]
  [Arguments("https://relay.example.com")]
  [Arguments("https://relay.example.com/")]
  public async Task Migration_Preserves_Cng_Source_And_Replay_And_Requires_Quiescence(
      string relayUrl,
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsWindows())
    {
      return;
    }
    var root = CreateRoot();
    var sourceRoot = Path.Combine(root, "source");
    var targetRoot = Path.Combine(root, "target");
    Directory.CreateDirectory(sourceRoot);
    Directory.CreateDirectory(targetRoot);
    var sourceStore = new SupportNodeIdentityStore(
        Path.Combine(sourceRoot, "identity-state"),
        new WindowsCngSupportNodeKeyProvider());
    var targetStore = CreateLinuxStore(
        Path.Combine(targetRoot, "identity-state"),
        new FakeUnixFilePermissions());
    try
    {
      var source = await CreateActiveIdentityAsync(
          sourceStore, sourceRoot, cancellationToken);
      var sourceReplay = new AgentReplayCache(source.ReplayRoot);
      var sessionId = Guid.NewGuid();
      var rejectedSessionId = Guid.NewGuid();
      using var signing = source.OpenSigningKey();
      using var encryption = SupportKeyFactory.ImportRsaPublicKey(
          source.DashboardResultEncryptionPublicKeySpki);
      var cached = SupportEnvelopeCryptography.Seal(
          [1, 2, 3], encryption, signing,
          source.NodeId.ToString("N"), "dashboard-result-v1");
      sourceReplay.ClaimNonce("migration-fixture-nonce");
      sourceReplay.StoreResult(sessionId, cached);
      sourceReplay.StoreRejection(
          rejectedSessionId, SupportRequestRejectionDispositions.BrokerTimeout);
      string? replacementCredential = null;
      var responseRelayUrl = relayUrl;
      var finalizationRelayUrl = relayUrl;
      var responseStatus = HttpStatusCode.OK;
      var requests = 0;
      var preparationRequests = new List<string>();
      var client = new SupportDashboardIdentityClient(
          new TestHttpClientFactory(
              SupportDashboardIdentityHttpClientOptions.ClientName,
              async (httpRequest, token) =>
              {
                requests++;
                if (!httpRequest.RequestUri!.AbsolutePath.EndsWith(
                    "/finalize", StringComparison.Ordinal))
                {
                  preparationRequests.Add(
                      await httpRequest.Content!.ReadAsStringAsync(token));
                }
                return new HttpResponseMessage(responseStatus)
                {
                  Content = JsonContent.Create(new SupportIdentityCompletionResponse(
                      source.NodeId.ToString("D"),
                      source.DisplayName,
                      replacementCredential!,
                      httpRequest.RequestUri!.AbsolutePath.EndsWith(
                          "/finalize", StringComparison.Ordinal)
                              ? finalizationRelayUrl
                              : responseRelayUrl,
                      source.DashboardAuthorizationSigningPublicKeySpki,
                      source.DashboardResultEncryptionPublicKeySpki)),
                };
              }));
      var sourceOptions = new SupportAgentBootstrapOptions(
          sourceStore.RootPath, source.ReplayRoot,
          "support-pipe", "/run/pitcrew-support/broker.sock",
          null, null, null, null, false);
      var targetOptions = sourceOptions with
      {
        IdentityRoot = targetStore.RootPath,
        ReplayRoot = Path.Combine(targetRoot, "replay"),
      };
      var sourceMigration = new SupportIdentityMigration(
          sourceStore, client, sourceOptions, sourceRoot);
      var targetMigration = new SupportIdentityMigration(
          targetStore, client, targetOptions, targetRoot);
      var offered = await sourceMigration.ExecuteAsync("offer", cancellationToken);
      File.Copy(
          Path.Combine(sourceRoot, SupportIdentityMigration.OfferFileName),
          Path.Combine(targetRoot, SupportIdentityMigration.OfferFileName));
      var staged = await targetMigration.ExecuteAsync("stage", cancellationToken);
      var stagedAgain = await targetMigration.ExecuteAsync("stage", cancellationToken);
      var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
      var targetState = JsonSerializer.Deserialize<SupportIdentityMigrationState>(
          await File.ReadAllTextAsync(
              Path.Combine(targetRoot, SupportIdentityMigration.StateFileName),
              cancellationToken),
          jsonOptions)!;
      var target = await targetStore.LoadMigrationTargetAsync(
          targetState.RotationId, cancellationToken);
      replacementCredential = target!.TransportCredential;
      File.Copy(
          Path.Combine(targetRoot, SupportIdentityMigration.TargetRequestFileName),
          Path.Combine(sourceRoot, SupportIdentityMigration.TargetRequestFileName));
      var requestPath = Path.Combine(
          sourceRoot, SupportIdentityMigration.TargetRequestFileName);
      var originalRequest = await File.ReadAllTextAsync(requestPath, cancellationToken);
      var request = JsonSerializer.Deserialize<SupportIdentityMigrationRequest>(
          originalRequest, jsonOptions)!;
      using var alteredEncryption = RSA.Create(3072);
      await File.WriteAllTextAsync(
          requestPath,
          JsonSerializer.Serialize(
              request with
              {
                TargetEncryptionPublicKeySpki =
                    SupportBase64Url.Encode(alteredEncryption.ExportSubjectPublicKeyInfo()),
              }, jsonOptions),
          cancellationToken);
      var alteredProof = await sourceMigration.ExecuteAsync("prepare", cancellationToken);
      var sourceBeforePrepare = await sourceStore.LoadActiveAsync(cancellationToken);
      await File.WriteAllTextAsync(requestPath, originalRequest, cancellationToken);
      var offerPath = Path.Combine(sourceRoot, SupportIdentityMigration.OfferFileName);
      var originalOffer = await File.ReadAllTextAsync(offerPath, cancellationToken);
      var substitutedOffer = JsonSerializer.Deserialize<SupportIdentityMigrationOffer>(
          originalOffer, jsonOptions)! with
      {
        DashboardUrl = "https://substituted-dashboard.invalid/",
      };
      var substitutedDigest = Convert.ToHexString(SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(substitutedOffer, jsonOptions)));
      using var sourceEncryption = SupportKeyFactory.ImportRsaPublicKey(
          substitutedOffer.SourceEncryptionPublicKeySpki);
      using var targetSigning = targetStore.KeyProvider.OpenSigningKey(
          target.DirectoryPath, target.Keys);
      var substitutedEnvelope = SupportEnvelopeCryptography.Seal(
          JsonSerializer.SerializeToUtf8Bytes(
              new SupportIdentityMigrationAuthorization(
                  request.RotationId, substitutedDigest,
                  request.TargetSigningPublicKeySpki,
                  request.TargetEncryptionPublicKeySpki,
                  replacementCredential), jsonOptions),
          sourceEncryption, targetSigning,
          request.RotationId.ToString("N"), source.NodeId.ToString("N"));
      await File.WriteAllTextAsync(
          offerPath, JsonSerializer.Serialize(substitutedOffer, jsonOptions),
          cancellationToken);
      await File.WriteAllTextAsync(
          requestPath,
          JsonSerializer.Serialize(
              request with
              {
                OfferDigest = substitutedDigest,
                CredentialEnvelope = substitutedEnvelope,
              }, jsonOptions),
          cancellationToken);
      var substitutedOrigin =
          await sourceMigration.ExecuteAsync("prepare", cancellationToken);
      var fencedBySubstitution = sourceStore.IsMigrationFenced;
      await File.WriteAllTextAsync(offerPath, originalOffer, cancellationToken);
      await File.WriteAllTextAsync(requestPath, originalRequest, cancellationToken);
      responseStatus = HttpStatusCode.ServiceUnavailable;
      var rejectedHttp = await sourceMigration.ExecuteAsync("prepare", cancellationToken);
      await Assert.That(rejectedHttp).IsEqualTo("source-prepare-http-rejected-503");
      responseStatus = HttpStatusCode.OK;
      foreach (var mismatchedRelayUrl in new[]
      {
        "https://other-relay.example.com",
        "https://relay.example.com:444/",
        "https://relay.example.com/path",
        "https://relay.example.com//",
        "https://relay.example.com/?scope=other",
        "https://relay.example.com/#other",
        "https://user@relay.example.com/",
        "http://relay.example.com/",
      })
      {
        responseRelayUrl = mismatchedRelayUrl;
        var mismatched = await sourceMigration.ExecuteAsync("prepare", cancellationToken);
        await Assert.That(mismatched).IsEqualTo("source-prepare-metadata-mismatch");
      }
      var preparing = JsonSerializer.Deserialize<SupportIdentityMigrationState>(
          await File.ReadAllTextAsync(
              Path.Combine(sourceStore.RootPath, SupportNodeIdentityStore.MigrationFenceFileName),
              cancellationToken), jsonOptions)!;
      var retriedRequest = await File.ReadAllTextAsync(requestPath, cancellationToken);
      var activeAfterRejection = await sourceStore.LoadActiveAsync(cancellationToken);
      await Assert.That(preparing.RotationId).IsEqualTo(request.RotationId);
      await Assert.That(preparing.Phase).IsEqualTo("preparing");
      await Assert.That(retriedRequest).IsEqualTo(originalRequest);
      await Assert.That(activeAfterRejection).IsNull();
      await Assert.That(File.Exists(
          Path.Combine(sourceRoot, SupportIdentityMigration.TransferFileName)))
          .IsFalse().Because("uncertain or mismatched preparation must not publish a capsule");
      responseRelayUrl = relayUrl;
      var prepared = await sourceMigration.ExecuteAsync("prepare", cancellationToken);
      var preparedAgain = await sourceMigration.ExecuteAsync("prepare", cancellationToken);
      var fencedSource = await sourceStore.LoadActiveAsync(cancellationToken);
      var retainedSource = await sourceStore.LoadMigrationSourceAsync(cancellationToken);
      var blockedRotation = await sourceStore.StageRotationAsync(cancellationToken);
      File.Copy(
          Path.Combine(sourceRoot, SupportIdentityMigration.TransferFileName),
          Path.Combine(targetRoot, SupportIdentityMigration.TransferFileName));
      var withoutAcknowledgement =
          await targetMigration.ExecuteAsync("activate", cancellationToken);
      await File.WriteAllTextAsync(
          Path.Combine(targetRoot, "identity-migration-source-quiesced.json"),
          JsonSerializer.Serialize(
              targetState with { Phase = "source-quiesced" }, jsonOptions),
          cancellationToken);
      finalizationRelayUrl = "https://relay.example.com:444/";
      var mismatchedFinalization =
          await targetMigration.ExecuteAsync("activate", cancellationToken);
      await Assert.That(mismatchedFinalization).IsEqualTo("target-finalization-metadata-mismatch");
      await Assert.That(targetStore.IsMigrationFenced)
          .IsTrue().Because("mismatched finalization must not resume target polling");
      var pendingFinalization =
          await targetStore.GetPendingRotationFinalizationAsync(cancellationToken);
      await Assert.That(pendingFinalization!.RotationId).IsEqualTo(request.RotationId);
      finalizationRelayUrl = relayUrl;
      var activated = await targetMigration.ExecuteAsync("activate", cancellationToken);
      var activatedAgain = await targetMigration.ExecuteAsync("activate", cancellationToken);
      var native = await targetStore.LoadActiveAsync(cancellationToken);
      var nativeReplay = new AgentReplayCache(targetOptions.ReplayRoot);

      await Assert.That(offered).IsEqualTo("offer-created");
      await Assert.That(staged).IsEqualTo("target-staged");
      await Assert.That(stagedAgain).IsEqualTo("target-staged");
      await Assert.That(prepared).IsEqualTo("source-prepared");
      await Assert.That(alteredProof).IsEqualTo("target-proof-invalid");
      await Assert.That(substitutedOrigin).IsEqualTo("source-request-invalid");
      await Assert.That(fencedBySubstitution).IsFalse()
          .Because("even a valid target proof cannot substitute the source Dashboard origin");
      await Assert.That(sourceBeforePrepare!.NodeId).IsEqualTo(source.NodeId);
      await Assert.That(preparedAgain).IsEqualTo("source-prepared");
      await Assert.That(fencedSource).IsNull();
      await Assert.That(retainedSource!.NodeId).IsEqualTo(source.NodeId);
      await Assert.That(blockedRotation).IsNull();
      await Assert.That(withoutAcknowledgement).IsEqualTo("source-quiescence-required");
      await Assert.That(activated).IsEqualTo("target-active");
      await Assert.That(activatedAgain).IsEqualTo("target-active");
      await Assert.That(native!.NodeId).IsEqualTo(source.NodeId);
      await Assert.That(native.TenantId).IsEqualTo(source.TenantId);
      await Assert.That(nativeReplay.GetResultOrNull(sessionId)).IsEqualTo(cached);
      await Assert.That(nativeReplay.HasNonce("migration-fixture-nonce"))
          .IsTrue().Because("source nonce tombstones must survive provider replacement");
      await Assert.That(nativeReplay.GetRejectionOrNull(rejectedSessionId))
          .IsEqualTo(SupportRequestRejectionDispositions.BrokerTimeout);
      await Assert.That(requests).IsEqualTo(12);
      await Assert.That(preparationRequests.Count).IsEqualTo(10);
      foreach (var preparationRequest in preparationRequests)
      {
        await Assert.That(preparationRequest).IsEqualTo(preparationRequests[0]);
      }
      await Assert.That(sourceStore.IsMigrationFenced)
          .IsTrue().Because("the retained CNG source must never become a second authority");
      await Assert.That(targetStore.IsMigrationFenced)
          .IsFalse().Because("only exact finalized target activation removes its local fence");
    }
    finally
    {
      await sourceStore.RemoveAsync(
          SupportIdentityKeyRemovalChoice.DeleteKeys, CancellationToken.None);
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Migration_Replay_Rejects_Traversal_And_Conflicting_State(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var provider = new LinuxFileSupportNodeKeyProvider(new FakeUnixFilePermissions());
      var traversal = SupportIdentityMigrationReplay.Write(
          root,
          new Dictionary<string, string>
          {
            ["support-results/../identity.json"] = Convert.ToBase64String([1]),
          },
          provider);
      var sessionId = Guid.NewGuid();
      var path = $"support-rejections/{sessionId:N}.txt";
      var first = SupportIdentityMigrationReplay.Write(
          root,
          new Dictionary<string, string> { [path] = Convert.ToBase64String([1]) },
          provider);
      var conflict = SupportIdentityMigrationReplay.Write(
          root,
          new Dictionary<string, string> { [path] = Convert.ToBase64String([2]) },
          provider);
      var bytes = await File.ReadAllBytesAsync(
          Path.Combine(root, "support-rejections", $"{sessionId:N}.txt"),
          cancellationToken);

      await Assert.That(traversal).IsFalse()
          .Because("opaque transfer filenames cannot expand the read/write allowlist");
      await Assert.That(first).IsTrue()
          .Because("a fresh allowlisted replay entry is accepted");
      await Assert.That(conflict).IsFalse()
          .Because("a replay transfer may not replace an existing terminal outcome");
      await Assert.That(bytes).IsEquivalentTo(new byte[] { 1 });
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task DeleteKeys_Is_Idempotent_When_Identity_Is_Missing(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var store = CreateLinuxStore(
          root,
          new FakeUnixFilePermissions());
      Directory.CreateDirectory(root);
      await File.WriteAllTextAsync(
          Path.Combine(root, SupportNodeIdentityStore.MigrationFenceFileName),
          "{}",
          cancellationToken);

      bool removed;
      await using (var operationLock =
          await store.AcquireOperationLockAsync(cancellationToken))
      {
        removed = await store.RemoveAsync(
            SupportIdentityKeyRemovalChoice.DeleteKeys,
            cancellationToken);
      }
      store.DeleteEmptyRoot();

      await Assert.That(removed).IsTrue();
      await Assert.That(Directory.Exists(root)).IsFalse();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Fresh_Identity_Generates_Locally_And_Reloads_Without_Private_Output(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var store = CreateLinuxStore(root, permissions);
      var pending = await store.GetOrCreatePendingEnrollmentAsync(
          "tenant-a",
          "Support node",
          "https://dashboard.example.com/",
          Path.Combine(root, "replay"),
          "support-pipe",
          cancellationToken);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      var nodeId = Guid.NewGuid();
      var completed = await store.CompleteEnrollmentAsync(
          CreateEnrollmentCompletion(
              pending ?? throw new InvalidOperationException(
                  "Pending identity was not created."),
              nodeId,
              "pcs_node_fixture-transport-credential",
              dashboardKeys),
          cancellationToken);
      var reloaded = await CreateLinuxStore(root, permissions)
          .LoadActiveAsync(cancellationToken);
      var status = await store.GetStatusAsync(cancellationToken);
      var statusJson = JsonSerializer.Serialize(status);

      await Assert.That(pending).IsNotNull();
      await Assert.That(completed).IsTrue();
      await Assert.That(reloaded).IsNotNull();
      await Assert.That(reloaded!.NodeId).IsEqualTo(nodeId);
      await Assert.That(status.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.Active);
      await Assert.That(statusJson).DoesNotContain("transport-credential");
      await Assert.That(statusJson).DoesNotContain("Private");
      await Assert.That(statusJson).DoesNotContain("pk8");
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Linux_Owner_Only_Permissions_Are_Enforced_And_Validated(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var store = CreateLinuxStore(root, permissions);
      var pending = await store.GetOrCreatePendingEnrollmentAsync(
          "tenant-a",
          "Support node",
          "https://dashboard.example.com/",
          Path.Combine(root, "replay"),
          "support-pipe",
          cancellationToken) ??
          throw new InvalidOperationException("Pending identity was not created.");
      var identityPath = Path.Combine(root, "identity");
      var signingPath = Path.Combine(
          identityPath,
          pending.Keys.SigningKeyReference);

      await Assert.That(permissions.Get(identityPath))
          .IsEqualTo(
              UnixFileMode.UserRead |
              UnixFileMode.UserWrite |
              UnixFileMode.UserExecute);
      await Assert.That(permissions.Get(signingPath))
          .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);

      permissions.Set(
          signingPath,
          UnixFileMode.UserRead |
          UnixFileMode.UserWrite |
          UnixFileMode.GroupRead);
      var status = await store.GetStatusAsync(cancellationToken);

      await Assert.That(status.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.Invalid);

      permissions.Set(
          signingPath,
          UnixFileMode.UserRead |
          UnixFileMode.UserWrite);
      permissions.MarkForeignOwned(signingPath);
      var foreignOwnedStatus = await store.GetStatusAsync(cancellationToken);

      await Assert.That(foreignOwnedStatus.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.Invalid);
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Interrupted_Create_Is_Cleaned_Without_Activating_Half_Identity(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var provider = new LinuxFileSupportNodeKeyProvider(permissions);
      Directory.CreateDirectory(root);
      permissions.Set(
          root,
          UnixFileMode.UserRead |
          UnixFileMode.UserWrite |
          UnixFileMode.UserExecute);
      const string keySetId = "11111111111111111111111111111111";
      var interruptedPath = Path.Combine(root, $".create-{keySetId}");
      Directory.CreateDirectory(interruptedPath);
      provider.SecureDirectory(interruptedPath);
      provider.Generate(interruptedPath, keySetId);
      var store = new SupportNodeIdentityStore(root, provider);

      var status = await store.GetStatusAsync(cancellationToken);

      await Assert.That(status.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.Missing);
      await Assert.That(Directory.Exists(interruptedPath)).IsFalse();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Rotation_Preserves_Old_Identity_Until_Accepted_And_Recovers_Partial_Move(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var store = CreateLinuxStore(root, permissions);
      var original = await CreateActiveIdentityAsync(
          store,
          root,
          cancellationToken);
      var originalStatus = await store.GetStatusAsync(cancellationToken);
      var rotation = await store.StageRotationAsync(cancellationToken) ??
          throw new InvalidOperationException("Rotation was not staged.");
      var duringRotation = await CreateLinuxStore(root, permissions)
          .LoadActiveAsync(cancellationToken);

      await Assert.That(duringRotation).IsNotNull();
      await Assert.That(originalStatus.NodeSigningPublicKeySpki)
          .IsNotEqualTo(rotation.NodeSigningPublicKeySpki);

      var identityPath = Path.Combine(root, "identity");
      var backupPath = Path.Combine(root, $".backup-{rotation.RotationId:N}");
      Directory.Move(identityPath, backupPath);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      var committed = await store.CommitRotationAsync(
          rotation.RotationId,
          new SupportIdentityCompletionData(
              original.NodeId,
              "Support node",
              rotation.ReplacementTransportCredential,
              "https://relay.example.com/",
              dashboardKeys.AuthorizationSigning.PublicKeySubjectPublicKeyInfoBase64Url,
              dashboardKeys.ResultEncryption.PublicKeySubjectPublicKeyInfoBase64Url),
          cancellationToken);
      var reloaded = await CreateLinuxStore(root, permissions)
          .LoadActiveAsync(cancellationToken);
      var rotatedStatus = await store.GetStatusAsync(cancellationToken);
      var pendingFinalization =
          await store.GetPendingRotationFinalizationAsync(cancellationToken);
      var secondRotationBeforeFinalization =
          await store.StageRotationAsync(cancellationToken);
      var finalized = await store.CompleteRotationFinalizationAsync(
          rotation.RotationId,
          cancellationToken);
      var secondRotationAfterFinalization =
          await store.StageRotationAsync(cancellationToken);

      await Assert.That(committed).IsTrue();
      await Assert.That(reloaded).IsNotNull();
      await Assert.That(rotatedStatus.NodeSigningPublicKeySpki)
          .IsEqualTo(rotation.NodeSigningPublicKeySpki);
      await Assert.That(Directory.Exists(backupPath)).IsFalse();
      await Assert.That(pendingFinalization).IsNotNull();
      await Assert.That(secondRotationBeforeFinalization).IsNull();
      await Assert.That(finalized).IsTrue();
      await Assert.That(secondRotationAfterFinalization).IsNotNull();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Removal_Requires_Explicit_Preserve_Or_Delete_Key_Semantics(
      CancellationToken cancellationToken)
  {
    var preserveRoot = CreateRoot();
    var deleteRoot = CreateRoot();
    try
    {
      var preservePermissions = new FakeUnixFilePermissions();
      var preserveStore = CreateLinuxStore(preserveRoot, preservePermissions);
      await CreateActiveIdentityAsync(
          preserveStore,
          preserveRoot,
          cancellationToken);
      var preserved = await preserveStore.RemoveAsync(
          SupportIdentityKeyRemovalChoice.PreserveKeys,
          cancellationToken);
      var preservedStatus = await preserveStore.GetStatusAsync(cancellationToken);

      var deletePermissions = new FakeUnixFilePermissions();
      var deleteStore = CreateLinuxStore(deleteRoot, deletePermissions);
      await CreateActiveIdentityAsync(
          deleteStore,
          deleteRoot,
          cancellationToken);
      await deleteStore.StageRotationAsync(cancellationToken);
      var deleted = await deleteStore.RemoveAsync(
          SupportIdentityKeyRemovalChoice.DeleteKeys,
          cancellationToken);
      var deletedStatus = await deleteStore.GetStatusAsync(cancellationToken);

      await Assert.That(preserved).IsTrue();
      await Assert.That(preservedStatus.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.KeysPreserved);
      await Assert.That(preservedStatus.NodeSigningPublicKeySpki).IsNotNull();
      await Assert.That(deleted).IsTrue();
      await Assert.That(deletedStatus.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.Missing);
      await Assert.That(Directory.EnumerateDirectories(
          deleteRoot,
          ".rotation-*",
          SearchOption.TopDirectoryOnly)).IsEmpty();
    }
    finally
    {
      DeleteRoot(preserveRoot);
      DeleteRoot(deleteRoot);
    }
  }

  [Test]
  public async Task Disable_Prevents_A_Staged_Rotation_From_Resuming(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var store = CreateLinuxStore(root, permissions);
      await CreateActiveIdentityAsync(store, root, cancellationToken);
      var staged = await store.StageRotationAsync(cancellationToken);
      var disabled = await store.DisableAsync(cancellationToken);
      var resumed = await store.StageRotationAsync(cancellationToken);

      await Assert.That(staged).IsNotNull();
      await Assert.That(disabled).IsTrue();
      await Assert.That(resumed).IsNull();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Windows_Cng_Keys_Are_Persisted_Nonexportable_And_Deleted_Explicitly()
  {
    if (!OperatingSystem.IsWindows())
    {
      return;
    }
    var root = CreateRoot();
    Directory.CreateDirectory(root);
    var provider = new WindowsCngSupportNodeKeyProvider();
    var keySetId = Guid.NewGuid().ToString("N");
    try
    {
      provider.SecureDirectory(root);
      var descriptor = provider.Generate(root, keySetId);
      using var signing = CngKey.Open(
          descriptor.SigningKeyReference,
          CngProvider.MicrosoftSoftwareKeyStorageProvider);
      using var encryption = CngKey.Open(
          descriptor.EncryptionKeyReference,
          CngProvider.MicrosoftSoftwareKeyStorageProvider);

      await Assert.That(signing.ExportPolicy)
          .IsEqualTo(CngExportPolicies.None);
      await Assert.That(encryption.ExportPolicy)
          .IsEqualTo(CngExportPolicies.None);
      await Assert.That(provider.Validate(root, descriptor)).IsTrue();
    }
    finally
    {
      provider.DeleteKeySet(root, keySetId);
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Packaged_Rotate_Command_Completes_Prepare_And_Finalize(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    var replayRoot = Path.Combine(root, "replay");
    var manager = SupportNodeIdentityManager.CreateDefault(root);
    HttpListener? listener = null;
    try
    {
      var port = GetAvailableLoopbackPort();
      var dashboardUrl = $"http://127.0.0.1:{port}/";
      var active = await CreateActiveIdentityAsync(
          manager.Store,
          root,
          cancellationToken,
          dashboardUrl);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      listener = new HttpListener();
      listener.Prefixes.Add(dashboardUrl);
      listener.Start();
      var server = ServeRotationAsync(
          listener,
          active.NodeId,
          dashboardKeys,
          cancellationToken);
      var startInfo = new ProcessStartInfo(
          "dotnet",
          $"\"{typeof(SupportNodeIdentityStore).Assembly.Location}\" rotate")
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      };
      startInfo.Environment["PitCrewSupport__Agent__IdentityRoot"] = root;
      startInfo.Environment["PitCrewSupport__Agent__ReplayRoot"] = replayRoot;
      startInfo.Environment["PitCrewSupport__Agent__PipeName"] =
          "support-command-test";
      await using var exclusion = await manager.Store.AcquireOperationLockAsync(
          cancellationToken);
      using var process = Process.Start(startInfo) ??
          throw new InvalidOperationException("Rotation command did not start.");
      await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
      var waitedForExclusion = !process.HasExited;
      await exclusion.DisposeAsync();
      var standardOutput = await process.StandardOutput.ReadToEndAsync(
          cancellationToken);
      var standardError = await process.StandardError.ReadToEndAsync(
          cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      await server;
      var pending = await manager.Store.GetPendingRotationFinalizationAsync(
          cancellationToken);

      await Assert.That(process.ExitCode).IsEqualTo(0);
      await Assert.That(waitedForExclusion).IsTrue();
      await Assert.That(standardOutput).Contains("\"status\":\"Succeeded\"");
      await Assert.That(standardError).DoesNotContain("credential");
      await Assert.That(pending).IsNull();
    }
    finally
    {
      listener?.Close();
      await manager.Store.RemoveAsync(
          SupportIdentityKeyRemovalChoice.DeleteKeys,
          CancellationToken.None);
      DeleteRoot(root);
    }
  }

  private static SupportNodeIdentityStore CreateLinuxStore(
      string root,
      IUnixFilePermissions permissions) =>
      new(root, new LinuxFileSupportNodeKeyProvider(permissions));

  private static async Task<StoredSupportNodeIdentity> CreateActiveIdentityAsync(
      SupportNodeIdentityStore store,
      string root,
      CancellationToken cancellationToken,
      string dashboardUrl = "https://dashboard.example.com/")
  {
    var pending = await store.GetOrCreatePendingEnrollmentAsync(
        "tenant-a",
        "Support node",
        dashboardUrl,
        Path.Combine(root, "replay"),
        "support-pipe",
        cancellationToken) ?? throw new InvalidOperationException(
            "Pending identity was not created.");
    var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
    var nodeId = Guid.NewGuid();
    await store.CompleteEnrollmentAsync(
        CreateEnrollmentCompletion(
            pending,
            nodeId,
            "pcs_node_fixture-transport-credential",
            dashboardKeys),
        cancellationToken);
    return await store.LoadActiveAsync(cancellationToken) ??
        throw new InvalidOperationException("Active identity was not loaded.");
  }

  private static string CreateRoot() =>
      Path.Combine(
          AppContext.BaseDirectory,
          $"support-identity-{Guid.NewGuid():N}");

  private static int GetAvailableLoopbackPort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
  }

  private static async Task ServeRotationAsync(
      HttpListener listener,
      Guid nodeId,
      SupportDashboardKeySet dashboardKeys,
      CancellationToken cancellationToken)
  {
    for (var requestIndex = 0; requestIndex < 2; requestIndex++)
    {
      var context = await listener.GetContextAsync().WaitAsync(
          cancellationToken);
      using var document = await JsonDocument.ParseAsync(
          context.Request.InputStream,
          cancellationToken: cancellationToken);
      var propertyName = requestIndex == 0
          ? "replacementTransportCredential"
          : "currentTransportCredential";
      var credential = document.RootElement.GetProperty(propertyName)
          .GetString() ??
          throw new InvalidOperationException(
              "Rotation credential was absent.");
      var response = JsonSerializer.SerializeToUtf8Bytes(
          new SupportIdentityCompletionResponse(
              nodeId.ToString("D"),
              "Support node",
              credential,
              "https://relay.example.com/",
              dashboardKeys.AuthorizationSigning
                  .PublicKeySubjectPublicKeyInfoBase64Url,
              dashboardKeys.ResultEncryption
                  .PublicKeySubjectPublicKeyInfoBase64Url),
          new JsonSerializerOptions(JsonSerializerDefaults.Web));
      context.Response.StatusCode = (int)HttpStatusCode.OK;
      context.Response.ContentType = "application/json";
      context.Response.ContentLength64 = response.Length;
      await context.Response.OutputStream.WriteAsync(
          response,
          cancellationToken);
      context.Response.Close();
    }
  }

  private static SupportEnrollmentCompletionData CreateEnrollmentCompletion(
      PendingSupportNodeIdentity pending,
      Guid nodeId,
      string transportCredential,
      SupportDashboardKeySet dashboardKeys)
  {
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        new EnrollmentCredentialPayload(
            "support-enrollment-credential-v1",
            nodeId,
            pending.CompletionId,
            transportCredential),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    try
    {
      using var nodeEncryption = SupportKeyFactory.ImportRsaPublicKey(
          pending.Keys.EncryptionPublicKeySpki);
      using var dashboardSigning = SupportKeyFactory.ImportEcdsaPrivateKey(
          dashboardKeys.AuthorizationSigning.PrivateKeyPkcs8Base64Url);
      var envelope = SupportEnvelopeCryptography.Seal(
          payload,
          nodeEncryption,
          dashboardSigning,
          "dashboard-support-auth-v1",
          nodeId.ToString("N"));
      return new SupportEnrollmentCompletionData(
          nodeId,
          pending.DisplayName,
          envelope,
          "https://relay.example.com/",
          dashboardKeys.AuthorizationSigning.PublicKeySubjectPublicKeyInfoBase64Url,
          dashboardKeys.ResultEncryption.PublicKeySubjectPublicKeyInfoBase64Url);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payload);
    }
  }

  private static void DeleteRoot(string root)
  {
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private sealed record EnrollmentCredentialPayload(
      string Schema,
      Guid NodeId,
      Guid CompletionId,
      string TransportCredential);
}
