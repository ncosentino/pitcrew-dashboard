using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using PitCrew.Support.Broker.App;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Broker.App.Tests;

public sealed class SupportDiagnosticsBrokerTests
{
  [Test]
  public async Task Broker_Rejects_Non_Allowlisted_Mode_And_Profile(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);

      var invalidMode = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              "Shell",
              "default",
              "0123456789abcdef"),
          cancellationToken);
      var invalidProfile = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "other",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(invalidMode.Status)
          .IsEqualTo(SupportBrokerStatus.InvalidMode);
      await Assert.That(invalidProfile.Status)
          .IsEqualTo(SupportBrokerStatus.InvalidProfile);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Derives_Only_Locally_Allowlisted_Profile(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.HostPressure,
              null,
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.Succeeded);
      await Assert.That(result.Response).IsNotNull();
      await Assert.That(
          result.Response!.Report.GetProperty("profile").GetString())
          .IsEqualTo("default");
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Executes_Only_Fixed_FileOnly_Collector(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);
      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.HostPressure,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.Succeeded);
      await Assert.That(result.Response).IsNotNull();
      await Assert.That(result.Response!.Markdown)
          .IsEqualTo("# Diagnostics");
      await Assert.That(
          result.Response.Report.GetProperty("mode").GetString())
          .IsEqualTo(SupportDiagnosticModes.HostPressure);
      await Assert.That(
          result.Response.Report.GetProperty("fileOnly").GetBoolean())
          .IsTrue()
          .Because("the broker always supplies -FileOnly to the fixed collector");
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Rejects_Missing_Dedicated_Evidence_Directory(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var policy = SupportEvidencePolicy.Load();
      Directory.Delete(
          Path.Combine(
              root,
              ".pitcrew-state",
              "default",
              policy.ProfileEvidenceDirectory),
          recursive: true);
      await File.WriteAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              "default",
              "observed-state.json"),
          "{}",
          cancellationToken);
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.EvidenceAccessDenied);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Allows_Atomic_Temporary_Evidence_File(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var policy = SupportEvidencePolicy.Load();
      await File.WriteAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              "default",
              policy.ProfileEvidenceDirectory,
              ".observed-state.test.tmp"),
          "{}",
          cancellationToken);
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.Succeeded);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Rejects_Unexpected_Persistent_Evidence_File(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var policy = SupportEvidencePolicy.Load();
      await File.WriteAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              "default",
              policy.ProfileEvidenceDirectory,
              "unexpected-state.json"),
          "{}",
          cancellationToken);
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.EvidenceAccessDenied);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Rejects_Collector_Content_Hash_Drift(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);
      var collectorPath = Path.Combine(
          root,
          SupportEvidencePolicy.Load().CollectorRelativePath.Replace(
              '/',
              Path.DirectorySeparatorChar));
      await File.AppendAllTextAsync(
          collectorPath,
          Environment.NewLine,
          cancellationToken);

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.EvidenceAccessDenied);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Rejects_Oversized_Collector_Output(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var policy = SupportEvidencePolicy.Load();
      var collectorPath = Path.Combine(
          root,
          policy.CollectorRelativePath.Replace(
              '/',
              Path.DirectorySeparatorChar));
      const string collector =
          """
          param(
              [string]$PitCrewRoot,
              [switch]$FileOnly,
              [switch]$PassThruOnly,
              [string]$DiagnosticMode,
              [string]$PackageId,
              [string]$Profile)
          [pscustomobject]@{
              report = [pscustomobject]@{ payload = 'x' * 4194304 }
              markdown = '# Diagnostics'
          }
          """;
      await File.WriteAllTextAsync(
          collectorPath,
          collector,
          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
          cancellationToken);
      var hash = Convert.ToHexString(
          SHA256.HashData(
              await File.ReadAllBytesAsync(
                  collectorPath,
                  cancellationToken)))
          .ToLowerInvariant();
      var broker = new SupportDiagnosticsBroker(
          options,
          policy with { CollectorSha256 = hash });

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.ExecutionFailed);
      await Assert.That(result.Error)
          .IsEqualTo("The diagnostics collector returned an oversized response.");
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Evidence_Policy_Is_Exact_For_PitCrew_0_10_3()
  {
    var policy = SupportEvidencePolicy.Load();
    var allPaths = policy.InstallationSentinels
        .Concat(policy.ProfileProjectionFiles)
        .Concat(policy.ConnectorHealthFiles)
        .Append(policy.ProfileEvidenceDirectory)
        .Append(policy.CollectorRelativePath)
        .ToArray();

    await Assert.That(policy.PitCrewVersion).IsEqualTo("0.10.3");
    await Assert.That(policy.PitCrewCommit)
        .IsEqualTo("4fbafcafca1aa659a07b2f5deb96edc5d3eb3269");
    await Assert.That(policy.CollectorSha256)
        .IsEqualTo(
            "18ed0cdb53e288f981bf5cc49cb404a5129b98ac14faaa5a6cbcab07b3591580");
    await Assert.That(policy.CollectorHashCanonicalization)
        .IsEqualTo("utf8-lf");
    await Assert.That(policy.ProfileStateRootAccess)
        .IsEqualTo("enumerate-profile-directories-only");
    await Assert.That(policy.ProfileEvidenceDirectory)
        .IsEqualTo("support-evidence");
    await Assert.That(policy.WindowsEvidenceInheritance)
        .IsEqualTo("object-inherit-read-ace");
    await Assert.That(policy.LinuxEvidenceInheritance)
        .IsEqualTo("directory-read-and-default-file-read-acl");
    await Assert.That(policy.ProfileProjectionFiles)
        .IsEquivalentTo(
        [
            "desired-capacity.json",
            "acknowledged-capacity.json",
            "static-profile.json",
            "observed-state.json",
        ]);
    await Assert.That(policy.ConnectorHealthFiles)
        .IsEquivalentTo(
        [
            "connector-health.json",
            "connector-events.jsonl",
            "connector-health-acknowledgement.json",
        ]);
    await Assert.That(allPaths.Any(
        path => path.Contains(".env", StringComparison.OrdinalIgnoreCase)))
        .IsFalse()
        .Because("the broker must never receive environment-file access");
    await Assert.That(allPaths.Any(
        path => path.Contains("docker.sock", StringComparison.OrdinalIgnoreCase)))
        .IsFalse()
        .Because("the broker must never receive Docker access");
  }

  [Test]
  public async Task Collector_Hash_Canonicalizes_Line_Endings(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var collectorPath = Path.Combine(
        root,
        SupportEvidencePolicy.Load().CollectorRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));
    try
    {
      var text = await File.ReadAllTextAsync(
          collectorPath,
          cancellationToken);
      await File.WriteAllTextAsync(
          collectorPath,
          text.Replace("\r\n", "\n", StringComparison.Ordinal),
          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
          cancellationToken);
      var lfHash =
          SupportEvidenceAccessValidator.ComputeCollectorSha256(collectorPath);
      await File.WriteAllTextAsync(
          collectorPath,
          text.Replace("\r\n", "\n", StringComparison.Ordinal)
              .Replace("\n", "\r\n", StringComparison.Ordinal),
          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
          cancellationToken);
      var crlfHash =
          SupportEvidenceAccessValidator.ComputeCollectorSha256(collectorPath);

      await Assert.That(crlfHash).IsEqualTo(lfHash);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  [SupportedOSPlatform("linux")]
  public async Task Profile_Directory_Enumeration_Acl_Drift_Is_Reported(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsLinux())
    {
      return;
    }
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var stateRoot = Path.Combine(root, ".pitcrew-state");
    try
    {
      File.SetUnixFileMode(stateRoot, UnixFileMode.UserExecute);
      var broker = SupportBrokerTestHost.CreateBroker(
          SupportBrokerTestHost.CreateOptions(root, "unused"));

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.EvidenceAccessDenied);
    }
    finally
    {
      File.SetUnixFileMode(
          stateRoot,
          UnixFileMode.UserRead |
          UnixFileMode.UserWrite |
          UnixFileMode.UserExecute);
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  [SupportedOSPlatform("linux")]
  public async Task Evidence_Acl_Drift_Is_Reported_Without_Broadening(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsLinux())
    {
      return;
    }
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      const string projection = "observed-state.json";
      SupportBrokerTestHost.WriteProjection(root, projection);
      var projectionPath = Path.Combine(
          root,
          ".pitcrew-state",
          "default",
          SupportEvidencePolicy.Load().ProfileEvidenceDirectory,
          projection);
      File.SetUnixFileMode(projectionPath, UnixFileMode.None);
      var options = SupportBrokerTestHost.CreateOptions(root, "unused");
      var broker = SupportBrokerTestHost.CreateBroker(options);

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.EvidenceAccessDenied);
      await Assert.That(result.Error)
          .IsEqualTo(
              "Support evidence ACL drift prevents the broker from reading the exact allowlist.");
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  [SupportedOSPlatform("linux")]
  public async Task Linked_Evidence_Is_Rejected_As_Arbitrary_File_Access(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsLinux())
    {
      return;
    }
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var outsidePath = $"{root}-outside.json";
    try
    {
      await File.WriteAllTextAsync(
          outsidePath,
          "{}",
          cancellationToken);
      File.CreateSymbolicLink(
          Path.Combine(
              root,
              ".pitcrew-state",
              "default",
              SupportEvidencePolicy.Load().ProfileEvidenceDirectory,
              "observed-state.json"),
          outsidePath);
      var broker = SupportBrokerTestHost.CreateBroker(
          SupportBrokerTestHost.CreateOptions(root, "unused"));

      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.EvidenceAccessDenied);
    }
    finally
    {
      File.Delete(outsidePath);
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  [SupportedOSPlatform("linux")]
  public async Task Unix_Socket_Client_Receives_Broker_Response(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsLinux())
    {
      return;
    }
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var socketDirectory = SupportBrokerTestHost.CreateSocketDirectory();
    try
    {
      var socketPath = Path.Combine(socketDirectory, "broker.sock");
      var options = SupportBrokerTestHost.CreateOptions(root, socketPath);
      var broker = SupportBrokerTestHost.CreateBroker(options);
      using var server = new SupportBrokerUnixSocketServer(options, broker);
      server.Initialize();
      var client = new SupportBrokerUnixSocketClient(socketPath);
      var serverTask = server.RunOnceAsync(cancellationToken);

      var result = await client.ExecuteAsync(
          new SupportBrokerRequest(
              SupportDiagnosticModes.Full,
              "default",
              "0123456789abcdef"),
          cancellationToken);
      await serverTask;

      await Assert.That(result.Status)
          .IsEqualTo(SupportBrokerStatus.Succeeded);
      await Assert.That(result.Response).IsNotNull();
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(socketDirectory);
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  [SupportedOSPlatform("linux")]
  public async Task Unix_Socket_Mode_Drift_Stops_Accepting_Requests(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsLinux())
    {
      return;
    }
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var socketDirectory = SupportBrokerTestHost.CreateSocketDirectory();
    try
    {
      var socketPath = Path.Combine(socketDirectory, "broker.sock");
      var options = SupportBrokerTestHost.CreateOptions(root, socketPath);
      var broker = SupportBrokerTestHost.CreateBroker(options);
      using var server = new SupportBrokerUnixSocketServer(options, broker);
      server.Initialize();
      File.SetUnixFileMode(
          socketPath,
          UnixSocketAccessPolicy.RequiredMode |
          UnixFileMode.OtherRead);

      await Assert.That(
              async () => await server.RunOnceAsync(cancellationToken))
          .Throws<InvalidOperationException>()
          .WithMessage(
              "The support broker socket ownership or mode does not match the installed policy.");
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(socketDirectory);
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Unix_Peer_Uid_Mismatch_Is_Denied()
  {
    var credentials = new UnixPeerCredentials(123, 2001, 3001);

    var accepted = UnixPeerCredentialPolicy.IsExpected(
        credentials,
        expectedAgentUid: 2002);

    await Assert.That(accepted)
        .IsFalse()
        .Because("only the configured support-agent UID may use broker IPC");
  }

  [Test]
  [SupportedOSPlatform("linux")]
  public async Task Unix_Socket_Rejects_Mismatched_Peer_Credentials(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsLinux())
    {
      return;
    }
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var socketDirectory = SupportBrokerTestHost.CreateSocketDirectory();
    try
    {
      var socketPath = Path.Combine(socketDirectory, "broker.sock");
      var options = SupportBrokerTestHost.CreateOptions(root, socketPath) with
      {
        ExpectedAgentUid =
            UnixProcessIdentity.GetEffectiveUserId() + 1,
      };
      var broker = SupportBrokerTestHost.CreateBroker(options);
      using var server = new SupportBrokerUnixSocketServer(options, broker);
      server.Initialize();
      var client = new SupportBrokerUnixSocketClient(socketPath);
      var serverTask = server.RunOnceAsync(cancellationToken);

      await Assert.That(
              async () => await client.ExecuteAsync(
                  new SupportBrokerRequest(
                      SupportDiagnosticModes.Full,
                      "default",
                      "0123456789abcdef"),
                  cancellationToken))
          .Throws<IOException>();
      await serverTask;
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(socketDirectory);
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public async Task Windows_Named_Pipe_Validates_Impersonated_Client_Sid(
      CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsWindows())
    {
      return;
    }
    using var identity = WindowsIdentity.GetCurrent();
    var currentSid = identity.User ??
        throw new InvalidOperationException("The test process has no SID.");
    var pipeName = $"pitcrew-support-broker-test-{Guid.NewGuid():N}";
    var security = WindowsPipeAccessPolicy.Create(
        currentSid,
        currentSid);
    await using var server = NamedPipeServerStreamAcl.Create(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous,
        0,
        0,
        security,
        HandleInheritability.None,
        (PipeAccessRights)0);
    await using var client = new NamedPipeClientStream(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous,
        TokenImpersonationLevel.Identification);
    var connectionTask = server.WaitForConnectionAsync(cancellationToken);
    await client.ConnectAsync(cancellationToken);
    await connectionTask;
    var unexpectedSid = new SecurityIdentifier(
        WellKnownSidType.LocalServiceSid,
        null);

    var accepted = WindowsNamedPipePeerValidator.IsExpectedClient(
        server,
        unexpectedSid);

    await Assert.That(accepted)
        .IsFalse()
        .Because("the broker must compare the impersonated SID exactly");
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public async Task Windows_Pipe_Acl_Contains_Only_Product_And_Admin_Identities()
  {
    if (!OperatingSystem.IsWindows())
    {
      return;
    }
    var agentSid = new SecurityIdentifier(
        WellKnownSidType.LocalServiceSid,
        null);
    var brokerSid = new SecurityIdentifier(
        WellKnownSidType.NetworkServiceSid,
        null);
    var security = WindowsPipeAccessPolicy.Create(agentSid, brokerSid);
    var rules = security.GetAccessRules(
        includeExplicit: true,
        includeInherited: false,
        typeof(SecurityIdentifier));
    var accessRules = rules
        .Cast<PipeAccessRule>()
        .ToArray();
    var allowedSids = accessRules
        .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
        .ToArray();

    await Assert.That(allowedSids)
        .IsEquivalentTo(
        [
            agentSid.Value,
            brokerSid.Value,
            new SecurityIdentifier(
                WellKnownSidType.LocalSystemSid,
                null).Value,
            new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null).Value,
        ]);
    await Assert.That(allowedSids.Contains("S-1-1-0", StringComparer.Ordinal))
        .IsFalse()
        .Because("Everyone must not receive named-pipe access");
    await Assert.That(allowedSids.Contains("S-1-5-11", StringComparer.Ordinal))
        .IsFalse()
        .Because("Authenticated Users must not receive named-pipe access");
    await Assert.That(accessRules.All(rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            !rule.IsInherited))
        .IsTrue()
        .Because("the pipe ACL must contain only explicit allow rules");
    await Assert.That(accessRules.Single(rule =>
            Equals(rule.IdentityReference, agentSid)).PipeAccessRights)
        .IsEqualTo(PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize);
    foreach (var privilegedSid in new[]
    {
        brokerSid,
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null),
    })
    {
      await Assert.That(accessRules.Single(rule =>
              Equals(rule.IdentityReference, privilegedSid)).PipeAccessRights)
          .IsEqualTo(PipeAccessRights.FullControl);
    }
  }

  [Test]
  public async Task Broker_Response_Status_Is_String_Compatible_With_Transport_Agent(
      CancellationToken cancellationToken)
  {
    await using var stream = new MemoryStream();
    await SupportBrokerPipeCodec.WriteAsync(
        stream,
        new SupportBrokerExecution(
            SupportBrokerStatus.Succeeded,
            null,
            null),
        cancellationToken);
    stream.Position = 0;
    var lengthBytes = new byte[4];
    await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
    var payload = new byte[
        BinaryPrimitives.ReadInt32LittleEndian(lengthBytes)];
    await stream.ReadExactlyAsync(payload, cancellationToken);
    using var document = JsonDocument.Parse(payload);

    await Assert.That(
        document.RootElement.GetProperty("status").GetString())
        .IsEqualTo("Succeeded");
  }
}
