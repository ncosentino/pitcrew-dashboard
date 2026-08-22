using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportNodeIdentityStore
{
  private const int SchemaVersion = 1;
  private const string IdentityDirectoryName = "identity";
  private const string ManifestFileName = "identity.json";
  private const string CreatePrefix = ".create-";
  private const string RotationPrefix = ".rotation-";
  private const string BackupPrefix = ".backup-";
  private const string OperationLockFileName = ".identity-operation.lock";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);
  private readonly string _rootPath;
  private readonly ISupportNodeKeyProvider _keyProvider;

  public SupportNodeIdentityStore(
      string rootPath,
      ISupportNodeKeyProvider keyProvider)
  {
    _rootPath = Path.GetFullPath(rootPath);
    _keyProvider = keyProvider;
  }

  public async Task<SupportNodeIdentityStatus> GetStatusAsync(
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return InvalidStatus();
    }
    var manifest = await ReadManifestOrNullAsync(
        IdentityPath,
        cancellationToken);
    return manifest is null
        ? new SupportNodeIdentityStatus(
            Directory.Exists(IdentityPath)
                ? SupportNodeIdentityLifecycle.Invalid
                : SupportNodeIdentityLifecycle.Missing,
            null,
            null,
            null,
            null,
            null,
            null)
        : ToStatus(manifest, IsManifestValid(IdentityPath, manifest) &&
            HasRequiredCredential(IdentityPath, manifest)
            ? manifest.Lifecycle
            : SupportNodeIdentityLifecycle.Invalid);
  }

  public async Task<FileStream> AcquireOperationLockAsync(
      CancellationToken cancellationToken)
  {
    EnsureRoot();
    var path = Path.Combine(_rootPath, OperationLockFileName);
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        var stream = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        _keyProvider.SecureFile(path);
        return stream;
      }
      catch (IOException)
      {
        await Task.Delay(
            TimeSpan.FromMilliseconds(250),
            cancellationToken);
      }
    }
  }

  public async Task<PendingSupportNodeIdentity?> GetOrCreatePendingEnrollmentAsync(
      string tenantId,
      string displayName,
      string dashboardUrl,
      string replayRoot,
      string pipeName,
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return null;
    }
    var existing = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (existing is not null)
    {
      return existing.Lifecycle == SupportNodeIdentityLifecycle.PendingEnrollment &&
          string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal) &&
          string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal) &&
          string.Equals(existing.DashboardUrl, dashboardUrl, StringComparison.Ordinal) &&
          IsManifestValid(IdentityPath, existing)
              ? new PendingSupportNodeIdentity(
                  tenantId,
                  displayName,
                  dashboardUrl,
                  existing.EnrollmentCompletionId!.Value,
                  existing.Keys)
              : null;
    }
    if (Directory.Exists(IdentityPath))
    {
      return null;
    }
    EnsureRoot();
    var keySetId = Guid.NewGuid().ToString("N");
    var completionId = Guid.NewGuid();
    var stagePath = Path.Combine(_rootPath, $"{CreatePrefix}{keySetId}");
    Directory.CreateDirectory(stagePath);
    _keyProvider.SecureDirectory(stagePath);
    try
    {
      var keys = _keyProvider.Generate(stagePath, keySetId);
      var manifest = new StoredSupportNodeIdentityManifest(
          SchemaVersion,
          SupportNodeIdentityLifecycle.PendingEnrollment,
          keys,
          tenantId,
          null,
          displayName,
          dashboardUrl,
          null,
          null,
          null,
          replayRoot,
          pipeName,
          null,
          false,
          completionId);
      await WriteManifestAsync(stagePath, manifest, cancellationToken);
      Directory.Move(stagePath, IdentityPath);
      return new PendingSupportNodeIdentity(
          tenantId,
          displayName,
          dashboardUrl,
          completionId,
          keys);
    }
    catch
    {
      _keyProvider.DeleteKeySet(stagePath, keySetId);
      DeleteDirectoryIfExists(stagePath);
      throw;
    }
  }

  public async Task<bool> CompleteEnrollmentAsync(
      SupportEnrollmentCompletionData completion,
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return false;
    }
    var manifest = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (manifest is null ||
        manifest.Lifecycle != SupportNodeIdentityLifecycle.PendingEnrollment ||
        !IsManifestValid(IdentityPath, manifest) ||
        manifest.EnrollmentCompletionId is null)
    {
      return false;
    }
    var credential = ReadEnrollmentCredentialOrNull(
        manifest,
        completion);
    if (credential is null)
    {
      return false;
    }
    _keyProvider.WriteCredential(
        IdentityPath,
        manifest.Keys,
        credential);
    var completed = manifest with
    {
      Lifecycle = SupportNodeIdentityLifecycle.Active,
      NodeId = completion.NodeId,
      DisplayName = completion.DisplayName,
      RelayUrl = completion.RelayUrl,
      DashboardAuthorizationSigningPublicKeySpki =
          completion.DashboardAuthorizationSigningPublicKeySpki,
      DashboardResultEncryptionPublicKeySpki =
          completion.DashboardResultEncryptionPublicKeySpki,
      EnrollmentCompletionId = null,
    };
    await WriteManifestAsync(IdentityPath, completed, cancellationToken);
    return true;
  }

  public async Task<StoredSupportNodeIdentity?> LoadActiveAsync(
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return null;
    }
    var manifest = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (manifest is null ||
        manifest.Lifecycle != SupportNodeIdentityLifecycle.Active ||
        !IsManifestValid(IdentityPath, manifest))
    {
      return null;
    }
    var credential = _keyProvider.ReadCredential(IdentityPath, manifest.Keys);
    return string.IsNullOrWhiteSpace(credential)
        ? null
        : new StoredSupportNodeIdentity(
            IdentityPath,
            _keyProvider,
            manifest,
            credential);
  }

  public async Task<bool> DisableAsync(CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return false;
    }
    var manifest = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (manifest is null ||
        manifest.Lifecycle is not (
            SupportNodeIdentityLifecycle.Active or
            SupportNodeIdentityLifecycle.AuthorizationRejected) ||
        !IsManifestValid(IdentityPath, manifest))
    {
      return false;
    }
    await WriteManifestAsync(
        IdentityPath,
        manifest with { Lifecycle = SupportNodeIdentityLifecycle.Disabled },
        cancellationToken);
    return true;
  }

  public async Task<bool> MarkAuthorizationRejectedAsync(
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return false;
    }
    var manifest = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (manifest is null ||
        manifest.Lifecycle != SupportNodeIdentityLifecycle.Active ||
        !IsManifestValid(IdentityPath, manifest))
    {
      return false;
    }
    await WriteManifestAsync(
        IdentityPath,
        manifest with
        {
          Lifecycle = SupportNodeIdentityLifecycle.AuthorizationRejected,
        },
        cancellationToken);
    return true;
  }

  public async Task<bool> RemoveAsync(
      SupportIdentityKeyRemovalChoice keyChoice,
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return false;
    }
    var manifest = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (manifest is null)
    {
      if (keyChoice == SupportIdentityKeyRemovalChoice.DeleteKeys &&
          !Directory.Exists(IdentityPath))
      {
        if (Directory.Exists(_rootPath))
        {
          await DeleteRotationStagesAsync(cancellationToken);
        }
        return true;
      }
      return false;
    }
    if (!IsManifestValid(IdentityPath, manifest))
    {
      return false;
    }
    _keyProvider.DeleteCredential(IdentityPath);
    if (keyChoice == SupportIdentityKeyRemovalChoice.DeleteKeys)
    {
      await DeleteRotationStagesAsync(cancellationToken);
      _keyProvider.DeleteKeySet(
          IdentityPath,
          manifest.Keys.KeySetId);
      DeleteDirectoryIfExists(IdentityPath);
      return true;
    }
    var preserved = manifest with
    {
      Lifecycle = SupportNodeIdentityLifecycle.KeysPreserved,
      TenantId = null,
      NodeId = null,
      DisplayName = null,
      DashboardUrl = null,
      RelayUrl = null,
      DashboardAuthorizationSigningPublicKeySpki = null,
      DashboardResultEncryptionPublicKeySpki = null,
      RotationId = null,
      RotationAccepted = false,
      EnrollmentCompletionId = null,
    };
    await WriteManifestAsync(IdentityPath, preserved, cancellationToken);
    return true;
  }

  public async Task<SupportIdentityRotationPlan?> StageRotationAsync(
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return null;
    }
    var current = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (current is null ||
        current.Lifecycle != SupportNodeIdentityLifecycle.Active ||
        current.RotationId is not null ||
        !IsManifestValid(IdentityPath, current))
    {
      return null;
    }
    var existingStage = await GetRotationStageOrNullAsync(cancellationToken);
    if (existingStage is not null)
    {
      return await CreateRotationPlanAsync(
          existingStage.Value.Path,
          existingStage.Value.Manifest,
          cancellationToken);
    }
    var currentCredential = _keyProvider.ReadCredential(
        IdentityPath,
        current.Keys);
    if (string.IsNullOrWhiteSpace(currentCredential))
    {
      return null;
    }
    var rotationId = Guid.NewGuid();
    var keySetId = rotationId.ToString("N");
    var stagePath = Path.Combine(_rootPath, $"{RotationPrefix}{keySetId}");
    Directory.CreateDirectory(stagePath);
    _keyProvider.SecureDirectory(stagePath);
    try
    {
      var keys = _keyProvider.Generate(stagePath, keySetId);
      var replacementCredential = CreateTransportCredential();
      _keyProvider.WriteCredential(stagePath, keys, replacementCredential);
      var staged = current with
      {
        Lifecycle = SupportNodeIdentityLifecycle.RotationStaged,
        Keys = keys,
        RotationId = rotationId,
        RotationAccepted = false,
      };
      await WriteManifestAsync(stagePath, staged, cancellationToken);
      return new SupportIdentityRotationPlan(
          rotationId,
          current.NodeId!.Value,
          current.TenantId!,
          current.DashboardUrl!,
          currentCredential,
          replacementCredential,
          keys.SigningPublicKeySpki,
          keys.EncryptionPublicKeySpki);
    }
    catch
    {
      _keyProvider.DeleteKeySet(stagePath, keySetId);
      DeleteDirectoryIfExists(stagePath);
      throw;
    }
  }

  public async Task<PendingSupportIdentityRotation?>
      GetPendingRotationFinalizationAsync(
          CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return null;
    }
    var manifest = await ReadManifestOrNullAsync(
        IdentityPath,
        cancellationToken);
    if (manifest is null ||
        manifest.Lifecycle != SupportNodeIdentityLifecycle.Active ||
        manifest.RotationId is null ||
        !manifest.RotationAccepted ||
        !IsManifestValid(IdentityPath, manifest))
    {
      return null;
    }
    var credential = _keyProvider.ReadCredential(
        IdentityPath,
        manifest.Keys);
    return string.IsNullOrWhiteSpace(credential)
        ? null
        : new PendingSupportIdentityRotation(
            manifest.RotationId.Value,
            manifest.NodeId!.Value,
            manifest.TenantId!,
            manifest.DashboardUrl!,
            credential);
  }

  public async Task<bool> CompleteRotationFinalizationAsync(
      Guid rotationId,
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return false;
    }
    var manifest = await ReadManifestOrNullAsync(
        IdentityPath,
        cancellationToken);
    if (manifest is null ||
        manifest.Lifecycle != SupportNodeIdentityLifecycle.Active ||
        manifest.RotationId != rotationId ||
        !manifest.RotationAccepted ||
        !IsManifestValid(IdentityPath, manifest))
    {
      return false;
    }
    await WriteManifestAsync(
        IdentityPath,
        manifest with
        {
          RotationId = null,
          RotationAccepted = false,
        },
        cancellationToken);
    return true;
  }

  public async Task<bool> CommitRotationAsync(
      Guid rotationId,
      SupportIdentityCompletionData completion,
      CancellationToken cancellationToken)
  {
    if (!await RecoverAsync(cancellationToken))
    {
      return false;
    }
    var stagePath = RotationPath(rotationId);
    var staged = await ReadManifestOrNullAsync(stagePath, cancellationToken);
    if (staged is null ||
        staged.RotationId != rotationId ||
        staged.NodeId != completion.NodeId ||
        !IsManifestValid(stagePath, staged))
    {
      return false;
    }
    var replacementCredential = _keyProvider.ReadCredential(stagePath, staged.Keys);
    if (!string.Equals(
        replacementCredential,
        completion.TransportCredential,
        StringComparison.Ordinal))
    {
      return false;
    }
    var accepted = staged with
    {
      Lifecycle = SupportNodeIdentityLifecycle.Active,
      DisplayName = completion.DisplayName,
      RelayUrl = completion.RelayUrl,
      DashboardAuthorizationSigningPublicKeySpki =
          completion.DashboardAuthorizationSigningPublicKeySpki,
      DashboardResultEncryptionPublicKeySpki =
          completion.DashboardResultEncryptionPublicKeySpki,
      RotationAccepted = true,
    };
    await WriteManifestAsync(stagePath, accepted, cancellationToken);
    return await RecoverAsync(cancellationToken);
  }

  private string IdentityPath => Path.Combine(_rootPath, IdentityDirectoryName);

  private void EnsureRoot()
  {
    Directory.CreateDirectory(_rootPath);
    if (ContainsReparsePoint(_rootPath))
    {
      throw new InvalidOperationException(
          "The support identity root cannot contain symbolic links or reparse points.");
    }
    _keyProvider.SecureDirectory(_rootPath);
  }

  private async Task<bool> RecoverAsync(CancellationToken cancellationToken)
  {
    if (!Directory.Exists(_rootPath))
    {
      return true;
    }
    if (ContainsReparsePoint(_rootPath))
    {
      return false;
    }
    foreach (var createPath in Directory.EnumerateDirectories(
        _rootPath,
        $"{CreatePrefix}*",
        SearchOption.TopDirectoryOnly))
    {
      var keySetId = Path.GetFileName(createPath)[CreatePrefix.Length..];
      _keyProvider.DeleteKeySet(createPath, keySetId);
      DeleteDirectoryIfExists(createPath);
    }
    var acceptedStage = await GetAcceptedRotationStageOrNullAsync(cancellationToken);
    var backups = Directory.EnumerateDirectories(
        _rootPath,
        $"{BackupPrefix}*",
        SearchOption.TopDirectoryOnly).ToArray();
    if (acceptedStage is not null)
    {
      var backupPath = BackupPath(acceptedStage.Value.Manifest.RotationId!.Value);
      if (Directory.Exists(IdentityPath) && !Directory.Exists(backupPath))
      {
        Directory.Move(IdentityPath, backupPath);
      }
      if (!Directory.Exists(IdentityPath))
      {
        Directory.Move(acceptedStage.Value.Path, IdentityPath);
      }
    }
    if (!Directory.Exists(IdentityPath) && backups.Length > 0)
    {
      Directory.Move(backups[0], IdentityPath);
    }
    if (Directory.Exists(IdentityPath))
    {
      foreach (var backup in Directory.EnumerateDirectories(
          _rootPath,
          $"{BackupPrefix}*",
          SearchOption.TopDirectoryOnly))
      {
        await DeleteStoredKeyDirectoryAsync(backup, cancellationToken);
      }
    }
    return true;
  }

  private async Task<(string Path, StoredSupportNodeIdentityManifest Manifest)?>
      GetAcceptedRotationStageOrNullAsync(CancellationToken cancellationToken)
  {
    foreach (var stagePath in Directory.EnumerateDirectories(
        _rootPath,
        $"{RotationPrefix}*",
        SearchOption.TopDirectoryOnly))
    {
      var manifest = await ReadManifestOrNullAsync(stagePath, cancellationToken);
      if (manifest is not null && manifest.RotationAccepted)
      {
        return (stagePath, manifest);
      }
    }
    return null;
  }

  private async Task<(string Path, StoredSupportNodeIdentityManifest Manifest)?>
      GetRotationStageOrNullAsync(CancellationToken cancellationToken)
  {
    foreach (var stagePath in Directory.EnumerateDirectories(
        _rootPath,
        $"{RotationPrefix}*",
        SearchOption.TopDirectoryOnly))
    {
      var manifest = await ReadManifestOrNullAsync(stagePath, cancellationToken);
      if (manifest is not null && IsManifestValid(stagePath, manifest))
      {
        return (stagePath, manifest);
      }
    }
    return null;
  }

  private async Task<SupportIdentityRotationPlan?> CreateRotationPlanAsync(
      string stagePath,
      StoredSupportNodeIdentityManifest staged,
      CancellationToken cancellationToken)
  {
    var current = await ReadManifestOrNullAsync(IdentityPath, cancellationToken);
    if (current is null ||
        !IsManifestValid(IdentityPath, current) ||
        staged.RotationId is null)
    {
      return null;
    }
    var currentCredential = _keyProvider.ReadCredential(IdentityPath, current.Keys);
    var replacementCredential = _keyProvider.ReadCredential(stagePath, staged.Keys);
    return string.IsNullOrWhiteSpace(currentCredential) ||
        string.IsNullOrWhiteSpace(replacementCredential)
            ? null
            : new SupportIdentityRotationPlan(
                staged.RotationId.Value,
                current.NodeId!.Value,
                current.TenantId!,
                current.DashboardUrl!,
                currentCredential,
                replacementCredential,
                staged.Keys.SigningPublicKeySpki,
                staged.Keys.EncryptionPublicKeySpki);
  }

  private bool IsManifestValid(
      string directoryPath,
      StoredSupportNodeIdentityManifest manifest)
  {
    var manifestPath = Path.Combine(directoryPath, ManifestFileName);
    if (manifest.SchemaVersion != SchemaVersion ||
        !_keyProvider.IsFileSecure(manifestPath) ||
        !_keyProvider.Validate(directoryPath, manifest.Keys))
    {
      return false;
    }
    return manifest.Lifecycle switch
    {
      SupportNodeIdentityLifecycle.PendingEnrollment =>
          IsBounded(manifest.TenantId, 128) &&
          IsBounded(manifest.DisplayName, 128) &&
          manifest.EnrollmentCompletionId is not null &&
          IsAllowedOrigin(manifest.DashboardUrl),
      SupportNodeIdentityLifecycle.Active or
      SupportNodeIdentityLifecycle.Disabled or
      SupportNodeIdentityLifecycle.AuthorizationRejected or
      SupportNodeIdentityLifecycle.RotationStaged =>
          manifest.NodeId is not null &&
          IsBounded(manifest.TenantId, 128) &&
          IsAllowedOrigin(manifest.DashboardUrl) &&
          IsAllowedOrigin(manifest.RelayUrl) &&
          IsBounded(
              manifest.DashboardAuthorizationSigningPublicKeySpki,
              4096) &&
          IsBounded(
              manifest.DashboardResultEncryptionPublicKeySpki,
              4096),
      SupportNodeIdentityLifecycle.KeysPreserved => true,
      _ => false,
    };
  }

  private bool HasRequiredCredential(
      string directoryPath,
      StoredSupportNodeIdentityManifest manifest) =>
      manifest.Lifecycle is not (
          SupportNodeIdentityLifecycle.Active or
          SupportNodeIdentityLifecycle.Disabled or
          SupportNodeIdentityLifecycle.AuthorizationRejected or
          SupportNodeIdentityLifecycle.RotationStaged) ||
      !string.IsNullOrWhiteSpace(
          _keyProvider.ReadCredential(directoryPath, manifest.Keys));

  private async Task<StoredSupportNodeIdentityManifest?> ReadManifestOrNullAsync(
      string directoryPath,
      CancellationToken cancellationToken)
  {
    var path = Path.Combine(directoryPath, ManifestFileName);
    if (!File.Exists(path))
    {
      return null;
    }
    try
    {
      await using var stream = new FileStream(
          path,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          4096,
          FileOptions.Asynchronous | FileOptions.SequentialScan);
      return await JsonSerializer.DeserializeAsync<StoredSupportNodeIdentityManifest>(
          stream,
          _jsonOptions,
          cancellationToken);
    }
    catch (JsonException)
    {
      return null;
    }
    catch (IOException)
    {
      return null;
    }
    catch (UnauthorizedAccessException)
    {
      return null;
    }
  }

  private async Task WriteManifestAsync(
      string directoryPath,
      StoredSupportNodeIdentityManifest manifest,
      CancellationToken cancellationToken)
  {
    var path = Path.Combine(directoryPath, ManifestFileName);
    var temporaryPath = $"{path}.{Guid.NewGuid():N}.new";
    await using (var stream = new FileStream(
        temporaryPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough))
    {
      await JsonSerializer.SerializeAsync(
          stream,
          manifest,
          _jsonOptions,
          cancellationToken);
      await stream.FlushAsync(cancellationToken);
    }
    _keyProvider.SecureFile(temporaryPath);
    File.Move(temporaryPath, path, overwrite: true);
    _keyProvider.SecureFile(path);
  }

  private async Task DeleteStoredKeyDirectoryAsync(
      string directoryPath,
      CancellationToken cancellationToken)
  {
    var manifest = await ReadManifestOrNullAsync(directoryPath, cancellationToken);
    var keySetId = manifest?.Keys.KeySetId ??
        Path.GetFileName(directoryPath)[BackupPrefix.Length..];
    _keyProvider.DeleteKeySet(directoryPath, keySetId);
    DeleteDirectoryIfExists(directoryPath);
  }

  private async Task DeleteRotationStagesAsync(
      CancellationToken cancellationToken)
  {
    foreach (var stagePath in Directory.EnumerateDirectories(
        _rootPath,
        $"{RotationPrefix}*",
        SearchOption.TopDirectoryOnly))
    {
      var manifest = await ReadManifestOrNullAsync(stagePath, cancellationToken);
      var keySetId = manifest?.Keys.KeySetId ??
          Path.GetFileName(stagePath)[RotationPrefix.Length..];
      _keyProvider.DeleteKeySet(stagePath, keySetId);
      DeleteDirectoryIfExists(stagePath);
    }
  }

  private SupportNodeIdentityStatus ToStatus(
      StoredSupportNodeIdentityManifest manifest,
      SupportNodeIdentityLifecycle lifecycle) =>
      new(
          lifecycle,
          manifest.NodeId,
          manifest.TenantId,
          manifest.Keys.Provider,
          manifest.Keys.KeySetId,
          manifest.Keys.SigningPublicKeySpki,
          manifest.Keys.EncryptionPublicKeySpki);

  private static SupportNodeIdentityStatus InvalidStatus() =>
      new(
          SupportNodeIdentityLifecycle.Invalid,
          null,
          null,
          null,
          null,
          null,
          null);

  private string? ReadEnrollmentCredentialOrNull(
      StoredSupportNodeIdentityManifest manifest,
      SupportEnrollmentCompletionData completion)
  {
    if (!string.Equals(
            completion.TransportCredentialEnvelope.SenderKeyId,
            "dashboard-support-auth-v1",
            StringComparison.Ordinal) ||
        !string.Equals(
            completion.TransportCredentialEnvelope.RecipientKeyId,
            completion.NodeId.ToString("N"),
            StringComparison.Ordinal))
    {
      return null;
    }
    byte[]? payload = null;
    try
    {
      using var dashboardSigningKey = SupportKeyFactory.ImportEcdsaPublicKey(
          completion.DashboardAuthorizationSigningPublicKeySpki);
      using var nodeEncryptionKey = _keyProvider.OpenEncryptionKey(
          IdentityPath,
          manifest.Keys);
      payload = SupportEnvelopeCryptography.OpenOrNull(
          completion.TransportCredentialEnvelope,
          dashboardSigningKey,
          nodeEncryptionKey);
      if (payload is null)
      {
        return null;
      }
      var credential = JsonSerializer.Deserialize<EnrollmentCredentialPayload>(
          payload,
          _jsonOptions);
      return credential is not null &&
          credential.Schema == "support-enrollment-credential-v1" &&
          credential.NodeId == completion.NodeId &&
          credential.CompletionId == manifest.EnrollmentCompletionId &&
          credential.TransportCredential.Length is >= 32 and <= 256
              ? credential.TransportCredential
              : null;
    }
    catch (CryptographicException)
    {
      return null;
    }
    catch (JsonException)
    {
      return null;
    }
    finally
    {
      if (payload is not null)
      {
        CryptographicOperations.ZeroMemory(payload);
      }
    }
  }

  private string RotationPath(Guid rotationId) =>
      Path.Combine(_rootPath, $"{RotationPrefix}{rotationId:N}");

  private string BackupPath(Guid rotationId) =>
      Path.Combine(_rootPath, $"{BackupPrefix}{rotationId:N}");

  private static bool IsBounded(string? value, int maximumLength) =>
      !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

  private static bool IsAllowedOrigin(string? value) =>
      Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
      string.IsNullOrEmpty(uri.UserInfo) &&
      string.IsNullOrEmpty(uri.Query) &&
      string.IsNullOrEmpty(uri.Fragment) &&
      uri.AbsolutePath == "/" &&
      (uri.Scheme == Uri.UriSchemeHttps ||
       uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

  private static string CreateTransportCredential()
  {
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return $"pcs_node_{SupportBase64Url.Encode(bytes)}";
  }

  private static bool ContainsReparsePoint(string path)
  {
    var current = Path.GetPathRoot(path);
    if (string.IsNullOrEmpty(current))
    {
      return true;
    }
    foreach (var segment in path[current.Length..].Split(
        Path.DirectorySeparatorChar,
        StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, segment);
      if (Directory.Exists(current) &&
          File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
      {
        return true;
      }
    }
    return false;
  }

  private static void DeleteDirectoryIfExists(string path)
  {
    if (Directory.Exists(path))
    {
      Directory.Delete(path, recursive: true);
    }
  }

  private sealed record EnrollmentCredentialPayload(
      string Schema,
      Guid NodeId,
      Guid CompletionId,
      string TransportCredential);
}
