using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using PitCrew.Support.Broker.App;

namespace PitCrew.Support.Broker.App.Tests;

public sealed class SupportBrokerStartupCoordinatorTests
{
  [Test]
  public async Task Preflight_Retries_Until_Evidence_Converges(
      CancellationToken cancellationToken)
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    var statusRoot = Path.Combine(
        AppContext.BaseDirectory,
        $"broker-status-{Guid.NewGuid():N}");
    Directory.CreateDirectory(statusRoot);
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(
          root,
          "unused");
      var timeProvider = new FakeTimeProvider(
          DateTimeOffset.Parse(
              "2026-08-26T00:00:00+00:00",
              CultureInfo.InvariantCulture));
      var coordinator = new SupportBrokerStartupCoordinator(
          new SupportBrokerRuntimeValidator(
              options,
              SupportBrokerTestHost.CreateBroker(options)),
          new SupportBrokerStartupStatusWriter(
              statusRoot,
              timeProvider),
          timeProvider);
      var evidenceRoot = Path.Combine(
          root,
          ".pitcrew-state",
          "default",
          SupportEvidencePolicy.Load()
              .ProfileEvidenceDirectory);
      Directory.Delete(
          evidenceRoot,
          recursive: true);

      var startup = coordinator.WaitUntilReadyAsync(
          cancellationToken);
      Directory.CreateDirectory(evidenceRoot);
      timeProvider.Advance(TimeSpan.FromSeconds(1));
      await startup;
      using var status = JsonDocument.Parse(
          await File.ReadAllTextAsync(
              Path.Combine(
                  statusRoot,
                  "broker-startup-status.json"),
              cancellationToken));

      await Assert.That(
              status.RootElement
                  .GetProperty("disposition")
                  .GetString())
          .IsEqualTo(
              SupportBrokerStartupDispositions.Ready);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
      SupportBrokerTestHost.DeleteDirectory(statusRoot);
    }
  }
}
