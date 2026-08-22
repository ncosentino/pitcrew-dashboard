using System.Net;

using Microsoft.Extensions.Configuration;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportNodeIdentityProvisionerTests
{
  [Test]
  public async Task Missing_Identity_Without_Enrollment_Reports_Legacy_Unavailable(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var provisioner = CreateProvisioner(
          root,
          enrollmentCode: null,
          _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

      var result = await provisioner.GetRuntimeOptionsAsync(cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(
              SupportAgentProvisioningStatus.LegacyConfigurationUnavailable);
      await Assert.That(result.Options).IsNull();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Pending_Identity_Without_Enrollment_Reports_Material_Unavailable(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var store = new SupportNodeIdentityStore(
          root,
          new LinuxFileSupportNodeKeyProvider(permissions));
      await store.GetOrCreatePendingEnrollmentAsync(
          "tenant-a",
          "Zephyr",
          "https://dashboard.example.com/",
          Path.Combine(root, "replay"),
          "support-pipe",
          cancellationToken);
      var provisioner = CreateProvisioner(
          root,
          enrollmentCode: null,
          _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
          store);

      var result = await provisioner.GetRuntimeOptionsAsync(cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(
              SupportAgentProvisioningStatus.EnrollmentMaterialUnavailable);
      await Assert.That(result.Options).IsNull();
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  [Test]
  public async Task Rejected_Enrollment_Preserves_Pending_Identity_And_Reports_Rejection(
      CancellationToken cancellationToken)
  {
    var root = CreateRoot();
    try
    {
      var permissions = new FakeUnixFilePermissions();
      var store = new SupportNodeIdentityStore(
          root,
          new LinuxFileSupportNodeKeyProvider(permissions));
      var provisioner = CreateProvisioner(
          root,
          "pcs_enroll_fixture-code-abcdefghijklmnopqrstuvwxyz",
          _ => new HttpResponseMessage(HttpStatusCode.Conflict),
          store);

      var result = await provisioner.GetRuntimeOptionsAsync(cancellationToken);
      var status = await store.GetStatusAsync(cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(SupportAgentProvisioningStatus.EnrollmentRejected);
      await Assert.That(result.Options).IsNull();
      await Assert.That(status.Lifecycle)
          .IsEqualTo(SupportNodeIdentityLifecycle.PendingEnrollment);
    }
    finally
    {
      DeleteRoot(root);
    }
  }

  private static SupportNodeIdentityProvisioner CreateProvisioner(
      string root,
      string? enrollmentCode,
      Func<HttpRequestMessage, HttpResponseMessage> respond,
      SupportNodeIdentityStore? store = null)
  {
    store ??= new SupportNodeIdentityStore(
        root,
        new LinuxFileSupportNodeKeyProvider(
            new FakeUnixFilePermissions()));
    var bootstrap = new SupportAgentBootstrapOptions(
        root,
        Path.Combine(root, "replay"),
        "support-pipe",
        "/tmp/support-pipe.sock",
        new Uri("https://dashboard.example.com/"),
        "tenant-a",
        "Zephyr",
        enrollmentCode,
        false);
    return new SupportNodeIdentityProvisioner(
        store,
        new SupportDashboardIdentityClient(
            new TestHttpClientFactory(
                SupportDashboardIdentityHttpClientOptions.ClientName,
                respond)),
        bootstrap,
        new ConfigurationBuilder().Build());
  }

  private static string CreateRoot() =>
      Path.Combine(
          AppContext.BaseDirectory,
          $"support-provisioning-{Guid.NewGuid():N}");

  private static void DeleteRoot(string root)
  {
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
