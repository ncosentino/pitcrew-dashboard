using System.Globalization;
using System.Text.Json;

using PitCrew.Support.Broker.App;

namespace PitCrew.Support.Broker.App.Tests;

public sealed class SupportBrokerStartupStatusWriterTests
{
  [Test]
  public async Task Writer_Persists_And_Clears_Bounded_Status()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"broker-startup-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-26T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var writer = new SupportBrokerStartupStatusWriter(
          root,
          new FixedTimeProvider(now));

      writer.Write(
          SupportBrokerStartupDispositions.Ready);
      using var document = JsonDocument.Parse(
          await File.ReadAllTextAsync(
              Path.Combine(
                  root,
                  "broker-startup-status.json")));
      var status = document.RootElement;

      await Assert.That(
              status.GetProperty("schemaVersion")
                  .GetInt32())
          .IsEqualTo(1);
      await Assert.That(
              status.GetProperty("disposition")
                  .GetString())
          .IsEqualTo(
              SupportBrokerStartupDispositions.Ready);
      await Assert.That(
              status.GetProperty("occurredAt")
                  .GetDateTimeOffset())
          .IsEqualTo(now);

      writer.Clear();
      await Assert.That(
              File.Exists(
                  Path.Combine(
                      root,
                      "broker-startup-status.json")))
          .IsFalse();
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  private sealed class FixedTimeProvider(
      DateTimeOffset _utcNow) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() =>
        _utcNow;
  }
}
