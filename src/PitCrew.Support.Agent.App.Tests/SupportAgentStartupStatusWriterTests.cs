using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportAgentStartupStatusWriterTests
{
  [Test]
  public async Task Write_Persists_Only_Bounded_Status_And_Clear_Removes_It()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-startup-status-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      var occurredAt = DateTimeOffset.Parse(
          "2026-08-21T23:00:00Z",
          CultureInfo.InvariantCulture);
      var writer = new SupportAgentStartupStatusWriter(
          new TestHostEnvironment(root),
          new FakeTimeProvider(occurredAt),
          NullLogger<SupportAgentStartupStatusWriter>.Instance);

      writer.Write(
          "identity-provisioning",
          "unhandled-exception",
          typeof(InvalidOperationException));

      var path = Path.Combine(
          root,
          "agent-startup-status.json");
      var json = await File.ReadAllTextAsync(path);
      using var document = JsonDocument.Parse(json);
      var status = document.RootElement;
      await Assert.That(status.GetProperty("schemaVersion").GetInt32())
          .IsEqualTo(1);
      await Assert.That(status.GetProperty("phase").GetString())
          .IsEqualTo("identity-provisioning");
      await Assert.That(status.GetProperty("disposition").GetString())
          .IsEqualTo("unhandled-exception");
      await Assert.That(status.GetProperty("exceptionType").GetString())
          .IsEqualTo(nameof(InvalidOperationException));
      await Assert.That(status.GetProperty("occurredAt").GetDateTimeOffset())
          .IsEqualTo(occurredAt);
      await Assert.That(json).DoesNotContain("Message");
      await Assert.That(json).DoesNotContain("StackTrace");

      writer.Clear();

      await Assert.That(File.Exists(path)).IsFalse();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
