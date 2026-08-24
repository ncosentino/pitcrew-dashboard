using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportEnrollmentFinalizationRequestWorkerTests
{
  [Test]
  public async Task Finalize_And_Rollback_Use_Active_Identity(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    var identityRoot = Path.Combine(root, "identity");
    var settingsPath = Path.Combine(root, "appsettings.json");
    var original = """
        {
          "PitCrewSupport": {
            "Agent": {
              "IdentityRoot": "identity",
              "ReplayRoot": "replay",
              "DashboardUrl": "http://localhost:5000/",
              "TenantId": "local",
              "DisplayName": "Canary",
              "EnrollmentCode": "one-time-value"
            }
          }
        }
        """u8.ToArray();
    var store = new SupportNodeIdentityStore(
        identityRoot,
        new LinuxFileSupportNodeKeyProvider(
            new FakeUnixFilePermissions()));
    var manager = new SupportNodeIdentityManager(store);
    try
    {
      await CreateActiveIdentityAsync(
          store,
          root,
          cancellationToken);
      await File.WriteAllBytesAsync(
          settingsPath,
          original,
          cancellationToken);
      await WriteRequestAsync(
          root,
          SupportEnrollmentFinalizationRequestWorker.FinalizeOperation,
          cancellationToken);

      var finalizeLifetime = new TestApplicationLifetime();
      var finalizeWorker = CreateWorker(
          manager,
          root,
          finalizeLifetime);
      var finalized =
          await finalizeWorker.ProcessRequestIfPresentAsync(
              cancellationToken);
      var finalizedStatus = ReadStatus(root);
      var backup = await File.ReadAllBytesAsync(
          Path.Combine(
              root,
              SupportAgentSettingsFinalizer.BackupFileName),
          cancellationToken);

      await WriteRequestAsync(
          root,
          SupportEnrollmentFinalizationRequestWorker.RollbackOperation,
          cancellationToken);
      var rollbackLifetime = new TestApplicationLifetime();
      var rollbackWorker = CreateWorker(
          manager,
          root,
          rollbackLifetime);
      var rolledBack =
          await rollbackWorker.ProcessRequestIfPresentAsync(
              cancellationToken);
      var rollbackStatus = ReadStatus(root);
      var restored = await File.ReadAllBytesAsync(
          settingsPath,
          cancellationToken);

      await Assert.That(finalized).IsTrue();
      await Assert.That(finalizeLifetime.StopRequested).IsTrue();
      await Assert.That(finalizedStatus.Phase)
          .IsEqualTo("enrollment-finalization");
      await Assert.That(finalizedStatus.Disposition)
          .IsEqualTo("succeeded");
      await Assert.That(backup).IsEquivalentTo(original);
      await Assert.That(rolledBack).IsTrue();
      await Assert.That(rollbackLifetime.StopRequested).IsTrue();
      await Assert.That(rollbackStatus.Disposition)
          .IsEqualTo("rollback-succeeded");
      await Assert.That(restored).IsEquivalentTo(original);
    }
    finally
    {
      await store.RemoveAsync(
          SupportIdentityKeyRemovalChoice.DeleteKeys,
          CancellationToken.None);
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Finalize_Requires_Active_Identity(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    var settingsPath = Path.Combine(root, "appsettings.json");
    var original = """
        {
          "PitCrewSupport": {
            "Agent": {
              "DashboardUrl": "http://localhost:5000/",
              "TenantId": "local",
              "DisplayName": "Canary",
              "EnrollmentCode": "one-time-value"
            }
          }
        }
        """u8.ToArray();
    var store = new SupportNodeIdentityStore(
        Path.Combine(root, "identity"),
        new LinuxFileSupportNodeKeyProvider(
            new FakeUnixFilePermissions()));
    try
    {
      await File.WriteAllBytesAsync(
          settingsPath,
          original,
          cancellationToken);
      await WriteRequestAsync(
          root,
          SupportEnrollmentFinalizationRequestWorker.FinalizeOperation,
          cancellationToken);
      var worker = CreateWorker(
          new SupportNodeIdentityManager(store),
          root,
          new TestApplicationLifetime());

      await worker.ProcessRequestIfPresentAsync(cancellationToken);
      var status = ReadStatus(root);
      var current = await File.ReadAllBytesAsync(
          settingsPath,
          cancellationToken);

      await Assert.That(status.Disposition)
          .IsEqualTo("active-identity-unavailable");
      await Assert.That(current).IsEquivalentTo(original);
      await Assert.That(
              File.Exists(
                  Path.Combine(
                      root,
                      SupportAgentSettingsFinalizer.BackupFileName)))
          .IsFalse();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  private static SupportEnrollmentFinalizationRequestWorker CreateWorker(
      SupportNodeIdentityManager manager,
      string root,
      TestApplicationLifetime lifetime)
  {
    var environment = new TestHostEnvironment
    {
      ContentRootPath = root,
    };
    return new SupportEnrollmentFinalizationRequestWorker(
        manager,
        new SupportAgentStartupStatusWriter(
            environment,
            TimeProvider.System,
            NullLogger<SupportAgentStartupStatusWriter>.Instance),
        environment,
        lifetime,
        TimeProvider.System);
  }

  private static async Task WriteRequestAsync(
      string root,
      string operation,
      CancellationToken cancellationToken)
  {
    var request = JsonSerializer.SerializeToUtf8Bytes(
        new
        {
          schemaVersion = 1,
          operation,
        },
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await File.WriteAllBytesAsync(
        Path.Combine(
            root,
            SupportEnrollmentFinalizationRequestWorker.RequestFileName),
        request,
        cancellationToken);
  }

  private static SupportAgentStartupStatus ReadStatus(string root) =>
      JsonSerializer.Deserialize<SupportAgentStartupStatus>(
          File.ReadAllText(
              Path.Combine(root, "agent-startup-status.json")),
          new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
      throw new InvalidOperationException("Startup status was unavailable.");

  private static async Task CreateActiveIdentityAsync(
      SupportNodeIdentityStore store,
      string root,
      CancellationToken cancellationToken)
  {
    var pending = await store.GetOrCreatePendingEnrollmentAsync(
        "tenant-a",
        "Support node",
        "https://dashboard.example.com/",
        Path.Combine(root, "replay"),
        "support-pipe",
        cancellationToken) ?? throw new InvalidOperationException(
            "Pending identity was not created.");
    var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
    var nodeId = Guid.NewGuid();
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        new EnrollmentCredentialPayload(
            "support-enrollment-credential-v1",
            nodeId,
            pending.CompletionId,
            "pcs_node_fixture-transport-credential"),
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
      await store.CompleteEnrollmentAsync(
          new SupportEnrollmentCompletionData(
              nodeId,
              pending.DisplayName,
              envelope,
              "https://relay.example.com/",
              dashboardKeys.AuthorizationSigning
                  .PublicKeySubjectPublicKeyInfoBase64Url,
              dashboardKeys.ResultEncryption
                  .PublicKeySubjectPublicKeyInfoBase64Url),
          cancellationToken);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payload);
    }
  }

  private static string CreateRoot()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"enrollment-finalization-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    return root;
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

  private sealed class TestApplicationLifetime : IHostApplicationLifetime
  {
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => _stopping.Token;

    public bool StopRequested => _stopping.IsCancellationRequested;

    public void StopApplication() => _stopping.Cancel();
  }

  private sealed class TestHostEnvironment : IHostEnvironment
  {
    public string EnvironmentName { get; set; } = "Test";

    public string ApplicationName { get; set; } = "Test";

    public string ContentRootPath { get; set; } = string.Empty;

    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
  }
}
