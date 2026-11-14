using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportIdentityMigration(
    SupportNodeIdentityStore _store,
    SupportDashboardIdentityClient _dashboard,
    SupportAgentBootstrapOptions _options,
    string _contentRoot)
{
  internal const string RequestFileName = "identity-migration-request.json";
  internal const string OfferFileName = "identity-migration-offer.json";
  internal const string TargetRequestFileName = "identity-migration-target.json";
  internal const string TransferFileName = "identity-migration-transfer.json";
  internal const string StateFileName = "identity-migration-state.json";
  private const int MaximumTransferBytes = 67_108_864;
  private static readonly JsonSerializerOptions _json =
      new(JsonSerializerDefaults.Web)
      {
        UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
      };

  internal async Task<string> ExecuteAsync(
      string operation,
      CancellationToken cancellationToken)
  {
    await using var operationLock =
        await _store.AcquireOperationLockAsync(cancellationToken);
    return operation switch
    {
      "offer" => await OfferAsync(cancellationToken),
      "stage" => await StageAsync(cancellationToken),
      "prepare" => await PrepareAsync(cancellationToken),
      "activate" => await ActivateAsync(cancellationToken),
      _ => "request-invalid",
    };
  }

  private async Task<string> OfferAsync(CancellationToken cancellationToken)
  {
    var identity = await _store.LoadActiveAsync(cancellationToken);
    var status = await _store.GetStatusAsync(cancellationToken);
    if (identity is null || status.KeyProvider != "windows-cng" ||
        status.NodeId is null ||
        _store.HasRotationStages ||
        await _store.GetPendingRotationFinalizationAsync(cancellationToken)
            is not null)
    {
      return "source-unavailable";
    }
    // The source public-key pin is transferred by the administrator, never
    // discovered from an unauthenticated target response.
    var offer = new SupportIdentityMigrationOffer(
        1,
        identity.NodeId,
        identity.TenantId,
        identity.DisplayName,
        identity.DashboardUrl.AbsoluteUri,
        identity.RelayUrl.AbsoluteUri,
        status.NodeSigningPublicKeySpki!,
        status.NodeEncryptionPublicKeySpki!,
        identity.DashboardAuthorizationSigningPublicKeySpki,
        identity.DashboardResultEncryptionPublicKeySpki);
    await WriteAsync(OfferFileName, offer, cancellationToken);
    return "offer-created";
  }

  private async Task<string> StageAsync(CancellationToken cancellationToken)
  {
    if (_store.KeyProvider.Name != "linux-pkcs8")
    {
      return "target-provider-invalid";
    }
    var offer = await ReadAsync<SupportIdentityMigrationOffer>(
        OfferFileName, 16_384, cancellationToken);
    if (!IsOfferValid(offer))
    {
      return "offer-invalid";
    }
    var state = await ReadAsync<SupportIdentityMigrationState>(
        StateFileName, 1024, cancellationToken);
    if (state is null && File.Exists(Path.Combine(_contentRoot, StateFileName)))
    {
      return "target-state-invalid";
    }
    var rotationId = state?.RotationId ?? Guid.NewGuid();
    if (state is not null &&
        (state.SchemaVersion != 1 ||
         state.Phase is not ("staging" or "staged")))
    {
      return "target-phase-invalid";
    }
    var target = await _store.LoadMigrationTargetAsync(
        rotationId, cancellationToken);
    if (target is null)
    {
      if (state?.Phase == "staged" ||
          File.Exists(Path.Combine(_contentRoot, TargetRequestFileName)))
      {
        return "target-unavailable";
      }
      state ??= new SupportIdentityMigrationState(1, rotationId, "", "staging");
      await WriteAsync(StateFileName, state, cancellationToken);
      await WriteFenceAsync(state, cancellationToken);
      if (
          !await _store.StageMigrationTargetAsync(
              offer!, rotationId, _options, cancellationToken))
      {
        return "target-unavailable";
      }
      target = await _store.LoadMigrationTargetAsync(
          rotationId, cancellationToken);
    }
    if (target is null)
    {
      return "target-unavailable";
    }
    var existing = await ReadAsync<SupportIdentityMigrationRequest>(
        TargetRequestFileName, 32_768, cancellationToken);
    if (state?.Phase == "staged")
    {
      return existing is not null &&
          existing.RotationId == rotationId &&
          existing.OfferDigest == Digest(offer) &&
          state.RequestDigest == Digest(existing)
              ? "target-staged"
              : "target-request-conflict";
    }
    if (existing is not null)
    {
      if (existing.RotationId != rotationId ||
          existing.OfferDigest != Digest(offer) ||
          existing.TargetSigningPublicKeySpki != target.Keys.SigningPublicKeySpki ||
          existing.TargetEncryptionPublicKeySpki != target.Keys.EncryptionPublicKeySpki)
      {
        return "target-request-conflict";
      }
      await WriteAsync(
          StateFileName,
          new SupportIdentityMigrationState(1, rotationId, Digest(existing), "staged"),
          cancellationToken);
      return "target-staged";
    }
    using var sourceEncryption =
        SupportKeyFactory.ImportRsaPublicKey(offer!.SourceEncryptionPublicKeySpki);
    using var targetSigning = _store.KeyProvider.OpenSigningKey(
        target.DirectoryPath, target.Keys);
    var credentialBytes = JsonSerializer.SerializeToUtf8Bytes(
        new SupportIdentityMigrationAuthorization(
            rotationId,
            Digest(offer),
            target.Keys.SigningPublicKeySpki,
            target.Keys.EncryptionPublicKeySpki,
            target.TransportCredential),
        _json);
    SupportEnvelope envelope;
    try
    {
      envelope = SupportEnvelopeCryptography.Seal(
          credentialBytes,
          sourceEncryption,
          targetSigning,
          rotationId.ToString("N"),
          offer.NodeId.ToString("N"));
    }
    finally
    {
      CryptographicOperations.ZeroMemory(credentialBytes);
    }
    var request = new SupportIdentityMigrationRequest(
        1,
        rotationId,
        Digest(offer),
        target.Keys.SigningPublicKeySpki,
        target.Keys.EncryptionPublicKeySpki,
        envelope);
    await WriteAsync(TargetRequestFileName, request, cancellationToken);
    await WriteAsync(
        StateFileName,
        new SupportIdentityMigrationState(1, rotationId, Digest(request), "staged"),
        cancellationToken);
    return "target-staged";
  }

  private async Task<string> PrepareAsync(CancellationToken cancellationToken)
  {
    var offer = await ReadAsync<SupportIdentityMigrationOffer>(
        OfferFileName, 16_384, cancellationToken);
    var request = await ReadAsync<SupportIdentityMigrationRequest>(
        TargetRequestFileName, 32_768, cancellationToken);
    var source = await _store.LoadMigrationSourceAsync(cancellationToken);
    var status = await _store.GetStatusAsync(cancellationToken);
    if (source is null || status.KeyProvider != "windows-cng" ||
        _store.HasRotationStages ||
        !IsOfferValid(offer) || request is null ||
        request.SchemaVersion != 1 || request.RotationId == Guid.Empty ||
        request.OfferDigest != Digest(offer) ||
        source.NodeId != offer!.NodeId || source.TenantId != offer.TenantId ||
        status.NodeSigningPublicKeySpki != offer.SourceSigningPublicKeySpki ||
        status.NodeEncryptionPublicKeySpki != offer.SourceEncryptionPublicKeySpki ||
        request.CredentialEnvelope.SenderKeyId != request.RotationId.ToString("N") ||
        request.CredentialEnvelope.RecipientKeyId != source.NodeId.ToString("N"))
    {
      return "source-request-invalid";
    }
    using var targetSigning =
        SupportKeyFactory.ImportEcdsaPublicKey(request.TargetSigningPublicKeySpki);
    using var targetEncryption =
        SupportKeyFactory.ImportRsaPublicKey(request.TargetEncryptionPublicKeySpki);
    using var sourceEncryption = source.OpenEncryptionKey();
    var bytes = SupportEnvelopeCryptography.OpenOrNull(
        request.CredentialEnvelope, targetSigning, sourceEncryption);
    if (bytes is null || bytes.Length is <= 0 or > 16_384 ||
        targetSigning.KeySize != 256 || targetEncryption.KeySize != 3072)
    {
      return "target-proof-invalid";
    }
    SupportIdentityMigrationAuthorization? authorization;
    try
    {
      authorization = JsonSerializer.Deserialize<SupportIdentityMigrationAuthorization>(
          bytes, _json);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(bytes);
    }
    if (authorization is null ||
        authorization.RotationId != request.RotationId ||
        authorization.OfferDigest != request.OfferDigest ||
        authorization.TargetSigningPublicKeySpki != request.TargetSigningPublicKeySpki ||
        authorization.TargetEncryptionPublicKeySpki != request.TargetEncryptionPublicKeySpki ||
        authorization.TransportCredential.Length is < 32 or > 256)
    {
      return "target-proof-invalid";
    }
    var credential = authorization.TransportCredential;
    var requestDigest = Digest(request);
    var fence = await ReadFenceAsync(cancellationToken);
    if (fence is null && _store.IsMigrationFenced)
    {
      return "source-fence-invalid";
    }
    if (fence is not null &&
        (fence.SchemaVersion != 1 || fence.RotationId != request.RotationId ||
         fence.RequestDigest != requestDigest))
    {
      return "source-fence-conflict";
    }
    if (fence?.Phase == "prepared")
    {
      if (!File.Exists(Path.Combine(_contentRoot, TransferFileName)))
      {
        return "source-transfer-missing";
      }
      await WriteAsync(StateFileName, fence, cancellationToken);
      return "source-prepared";
    }
    // Fence before the network side effect. An uncertain prepare is resumed
    // with the identical request; no failure path restarts source polling.
    await WriteFenceAsync(
        new SupportIdentityMigrationState(
            1, request.RotationId, requestDigest, "preparing"),
        cancellationToken);
    var completion = await _dashboard.PrepareRotationAsync(
        new SupportIdentityRotationPlan(
            request.RotationId,
            source.NodeId,
            source.TenantId,
            source.DashboardUrl.AbsoluteUri,
            source.TransportCredential,
            credential,
            request.TargetSigningPublicKeySpki,
            request.TargetEncryptionPublicKeySpki),
        cancellationToken);
    if (completion is null || completion.NodeId != source.NodeId ||
        completion.DisplayName != offer.DisplayName ||
        completion.TransportCredential != credential ||
        completion.RelayUrl != offer.RelayUrl ||
        completion.DashboardAuthorizationSigningPublicKeySpki !=
            offer.DashboardAuthorizationSigningPublicKeySpki ||
        completion.DashboardResultEncryptionPublicKeySpki !=
            offer.DashboardResultEncryptionPublicKeySpki)
    {
      return "source-prepare-pending";
    }
    var replay = SupportIdentityMigrationReplay.Read(source.ReplayRoot);
    if (replay is null)
    {
      return "source-replay-invalid";
    }
    var transfer = new SupportIdentityMigrationTransfer(
        1, request.RotationId, requestDigest, completion, replay);
    var payload = JsonSerializer.SerializeToUtf8Bytes(transfer, _json);
    using var sourceSigning = source.OpenSigningKey();
    try
    {
      if (payload.Length > MaximumTransferBytes / 2)
      {
        return "source-replay-budget-exceeded";
      }
      var envelope = SupportEnvelopeCryptography.Seal(
          payload, targetEncryption, sourceSigning,
          source.NodeId.ToString("N"), request.RotationId.ToString("N"));
      await WriteAsync(TransferFileName, envelope, cancellationToken);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payload);
    }
    await WriteFenceAsync(
        new SupportIdentityMigrationState(
            1, request.RotationId, requestDigest, "prepared"),
        cancellationToken);
    await WriteAsync(
        StateFileName,
        new SupportIdentityMigrationState(
            1, request.RotationId, requestDigest, "prepared"),
        cancellationToken);
    return "source-prepared";
  }

  private async Task<string> ActivateAsync(CancellationToken cancellationToken)
  {
    var state = await ReadAsync<SupportIdentityMigrationState>(
        StateFileName, 1024, cancellationToken);
    if (_store.KeyProvider.Name != "linux-pkcs8" || state is null ||
        state.SchemaVersion != 1 ||
        state.Phase is not ("staged" or "committed" or "active"))
    {
      return "target-phase-invalid";
    }
    // The installer creates this fixed acknowledgement only after an explicit
    // administrator assertion that both source services are stopped/disabled.
    var acknowledgement = await ReadAsync<SupportIdentityMigrationState>(
        "identity-migration-source-quiesced.json", 1024, cancellationToken);
    if (acknowledgement != (state with { Phase = "source-quiesced" }))
    {
      return "source-quiescence-required";
    }
    if (state.Phase == "active")
    {
      if (!await IsCompletedTargetAsync(state, cancellationToken))
      {
        return "target-phase-invalid";
      }
      File.Delete(Path.Combine(_store.RootPath, SupportNodeIdentityStore.MigrationFenceFileName));
      return "target-active";
    }
    var recoveredPending =
        await _store.GetPendingRotationFinalizationAsync(cancellationToken);
    if (state.Phase == "staged" && recoveredPending?.RotationId == state.RotationId)
    {
      state = state with { Phase = "committed" };
      await WriteAsync(StateFileName, state, cancellationToken);
    }
    if (state.Phase == "staged")
    {
      var target = await _store.LoadMigrationTargetAsync(
          state.RotationId, cancellationToken);
      var offer = await ReadAsync<SupportIdentityMigrationOffer>(
          OfferFileName, 16_384, cancellationToken);
      var envelope = await ReadAsync<SupportEnvelope>(
          TransferFileName, MaximumTransferBytes, cancellationToken);
      if (target is null || !IsOfferValid(offer) || envelope is null ||
          envelope.SenderKeyId != offer!.NodeId.ToString("N") ||
          envelope.RecipientKeyId != state.RotationId.ToString("N"))
      {
        return "target-transfer-invalid";
      }
      using var signing =
          SupportKeyFactory.ImportEcdsaPublicKey(offer.SourceSigningPublicKeySpki);
      using var encryption = _store.KeyProvider.OpenEncryptionKey(
          target.DirectoryPath, target.Keys);
      var payload = SupportEnvelopeCryptography.OpenOrNull(envelope, signing, encryption);
      if (payload is null)
      {
        return "target-transfer-invalid";
      }
      SupportIdentityMigrationTransfer? transfer;
      try
      {
        transfer = JsonSerializer.Deserialize<SupportIdentityMigrationTransfer>(
            payload, _json);
      }
      finally
      {
        CryptographicOperations.ZeroMemory(payload);
      }
      if (transfer is null || transfer.SchemaVersion != 1 ||
          transfer.RotationId != state.RotationId ||
          transfer.RequestDigest != state.RequestDigest ||
          transfer.Completion.NodeId != offer.NodeId ||
          transfer.Completion.DisplayName != offer.DisplayName ||
          transfer.Completion.TransportCredential != target.TransportCredential ||
          transfer.Completion.RelayUrl != offer.RelayUrl ||
          transfer.Completion.DashboardAuthorizationSigningPublicKeySpki !=
              offer.DashboardAuthorizationSigningPublicKeySpki ||
          transfer.Completion.DashboardResultEncryptionPublicKeySpki !=
              offer.DashboardResultEncryptionPublicKeySpki ||
          !SupportIdentityMigrationReplay.Write(
              _options.ReplayRoot, transfer.ReplayFiles, _store.KeyProvider))
      {
        return "target-transfer-invalid";
      }
      if (!await _store.CommitRotationAsync(
          state.RotationId, transfer.Completion, cancellationToken))
      {
        return "target-commit-pending";
      }
      state = state with { Phase = "committed" };
      await WriteAsync(StateFileName, state, cancellationToken);
    }
    var pending = await _store.GetPendingRotationFinalizationAsync(cancellationToken);
    if (pending is null || pending.RotationId != state.RotationId)
    {
      if (await IsCompletedTargetAsync(state, cancellationToken))
      {
        await WriteAsync(StateFileName, state with { Phase = "active" }, cancellationToken);
        File.Delete(Path.Combine(_store.RootPath, SupportNodeIdentityStore.MigrationFenceFileName));
        return "target-active";
      }
      return "target-finalization-pending";
    }
    var completed = await _dashboard.FinalizeRotationAsync(pending, cancellationToken);
    var committed = await _store.LoadMigrationSourceAsync(cancellationToken);
    if (completed is null || completed.NodeId != pending.NodeId ||
        completed.TransportCredential != pending.CurrentTransportCredential ||
        committed is null ||
        completed.DisplayName != committed.DisplayName ||
        completed.RelayUrl != committed.RelayUrl.AbsoluteUri ||
        completed.DashboardAuthorizationSigningPublicKeySpki !=
            committed.DashboardAuthorizationSigningPublicKeySpki ||
        completed.DashboardResultEncryptionPublicKeySpki !=
            committed.DashboardResultEncryptionPublicKeySpki ||
        !await _store.CompleteRotationFinalizationAsync(
            pending.RotationId, cancellationToken))
    {
      return "target-finalization-pending";
    }
    await WriteAsync(StateFileName, state with { Phase = "active" }, cancellationToken);
    File.Delete(Path.Combine(_store.RootPath, SupportNodeIdentityStore.MigrationFenceFileName));
    return "target-active";
  }

  private async Task<SupportIdentityMigrationState?> ReadFenceAsync(
      CancellationToken cancellationToken) =>
      await ReadPathAsync<SupportIdentityMigrationState>(
          Path.Combine(_store.RootPath, SupportNodeIdentityStore.MigrationFenceFileName),
          1024, cancellationToken);

  private async Task<bool> IsCompletedTargetAsync(
      SupportIdentityMigrationState state,
      CancellationToken cancellationToken)
  {
    if (!await _store.IsMigrationFinalizedAsync(cancellationToken))
    {
      return false;
    }
    var active = await _store.LoadMigrationSourceAsync(cancellationToken);
    var currentStatus = await _store.GetStatusAsync(cancellationToken);
    var request = await ReadAsync<SupportIdentityMigrationRequest>(
        TargetRequestFileName, 32_768, cancellationToken);
    return active is not null && request is not null &&
        request.RotationId == state.RotationId &&
        state.RequestDigest == Digest(request) &&
        currentStatus.NodeSigningPublicKeySpki == request.TargetSigningPublicKeySpki &&
        currentStatus.NodeEncryptionPublicKeySpki == request.TargetEncryptionPublicKeySpki;
  }

  private Task WriteFenceAsync(
      SupportIdentityMigrationState state,
      CancellationToken cancellationToken) =>
      WritePathAsync(
          Path.Combine(_store.RootPath, SupportNodeIdentityStore.MigrationFenceFileName),
          state, cancellationToken);

  private Task<T?> ReadAsync<T>(
      string fileName,
      int maximumBytes,
      CancellationToken cancellationToken) =>
      ReadPathAsync<T>(Path.Combine(_contentRoot, fileName), maximumBytes, cancellationToken);

  private static async Task<T?> ReadPathAsync<T>(
      string path,
      int maximumBytes,
      CancellationToken cancellationToken)
  {
    if (!File.Exists(path))
    {
      return default;
    }
    var file = new FileInfo(path);
    if (file.Length is <= 0 || file.Length > maximumBytes ||
        (file.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      return default;
    }
    await using var stream = File.OpenRead(path);
    try
    {
      return await JsonSerializer.DeserializeAsync<T>(
          stream, _json, cancellationToken);
    }
    catch (JsonException)
    {
      return default;
    }
  }

  private Task WriteAsync<T>(
      string fileName,
      T value,
      CancellationToken cancellationToken) =>
      WritePathAsync(Path.Combine(_contentRoot, fileName), value, cancellationToken);

  private async Task WritePathAsync<T>(
      string path,
      T value,
      CancellationToken cancellationToken)
  {
    var temporary = path + ".new";
    if (File.Exists(temporary) &&
        (File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
    {
      throw new IOException("Linked migration state is prohibited.");
    }
    await using (var stream = new FileStream(
        temporary, FileMode.Create, FileAccess.Write, FileShare.None,
        4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
    {
      // Windows public/opaque lifecycle output inherits the existing protected
      // content-root ACL so the privileged installer can transfer it. The
      // identity fence and all Linux state retain provider-only protection.
      if (_store.KeyProvider.Name == "linux-pkcs8" ||
          string.Equals(
              Path.GetDirectoryName(path), _store.RootPath, StringComparison.Ordinal))
      {
        _store.KeyProvider.SecureFile(temporary);
      }
      await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
      await stream.FlushAsync(cancellationToken);
    }
    File.Move(temporary, path, overwrite: true);
  }

  private static string Digest<T>(T value) =>
      Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, _json)));

  private static bool IsOfferValid(SupportIdentityMigrationOffer? offer)
  {
    if (offer is null || offer.SchemaVersion != 1 || offer.NodeId == Guid.Empty ||
        offer.TenantId.Length is < 1 or > 128 ||
        !Uri.TryCreate(offer.DashboardUrl, UriKind.Absolute, out var dashboard) ||
        !SupportAgentBootstrapOptions.IsAllowedOrigin(dashboard) ||
        !Uri.TryCreate(offer.RelayUrl, UriKind.Absolute, out var relay) ||
        !SupportAgentBootstrapOptions.IsAllowedOrigin(relay))
    {
      return false;
    }
    try
    {
      using var signing = SupportKeyFactory.ImportEcdsaPublicKey(offer.SourceSigningPublicKeySpki);
      using var encryption = SupportKeyFactory.ImportRsaPublicKey(offer.SourceEncryptionPublicKeySpki);
      using var dashboardSigning =
          SupportKeyFactory.ImportEcdsaPublicKey(offer.DashboardAuthorizationSigningPublicKeySpki);
      using var dashboardEncryption =
          SupportKeyFactory.ImportRsaPublicKey(offer.DashboardResultEncryptionPublicKeySpki);
      return signing.KeySize == 256 && encryption.KeySize == 3072 &&
          dashboardSigning.KeySize == 256 && dashboardEncryption.KeySize == 3072;
    }
    catch (CryptographicException)
    {
      return false;
    }
    catch (FormatException)
    {
      return false;
    }
  }
}
