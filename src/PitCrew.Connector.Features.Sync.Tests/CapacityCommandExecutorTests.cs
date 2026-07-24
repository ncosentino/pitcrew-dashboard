using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class CapacityCommandExecutorTests
{
  [Test]
  public async Task ExecuteAsync_Runs_Exact_CapacityOnly_Arguments(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          7,
          30,
          cancellationToken);
      var options = CapacityTestData.CreateOperatorOptions(root, 50);
      var resolver = new CapacityProfileResolver(
          Options.Create(options),
          NullLogger<CapacityProfileResolver>.Instance);
      var process = new FakeCapacityProcessRunner
      {
        Handler = async (_, token) =>
        {
          await CapacityTestData.WriteSingleRepositoryProfileAsync(
              root,
              8,
              40,
              token);
          return new CapacityProcessResult(0, false);
        },
      };
      var now = new DateTimeOffset(
          2026,
          7,
          24,
          12,
          0,
          0,
          TimeSpan.Zero);
      var executor = new CapacityCommandExecutor(
          resolver,
          process,
          Options.Create(options),
          new FixedTimeProvider(now),
          NullLogger<CapacityCommandExecutor>.Instance);

      var outcome = await executor.ExecuteAsync(
          new SetCapacityCommand(
              Guid.NewGuid(),
              "default",
              7,
              40,
              now.AddMinutes(5)),
          cancellationToken);

      await Assert.That(outcome.Status).IsEqualTo("succeeded");
      await Assert.That(outcome.AcceptedGeneration).IsEqualTo(8);
      await Assert.That(process.LastRequest).IsNotNull();
      await Assert.That(process.LastRequest!.Arguments)
          .Contains("-CapacityOnly");
      await Assert.That(process.LastRequest.Arguments)
          .Contains("https://github.com/example/project=40");
      await Assert.That(process.LastRequest.Arguments)
          .DoesNotContain("-Token");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Stale_Generation_Without_Process(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          8,
          30,
          cancellationToken);
      var options = CapacityTestData.CreateOperatorOptions(root, 50);
      var process = new FakeCapacityProcessRunner();
      var now = DateTimeOffset.UtcNow;
      var executor = new CapacityCommandExecutor(
          new CapacityProfileResolver(
              Options.Create(options),
              NullLogger<CapacityProfileResolver>.Instance),
          process,
          Options.Create(options),
          new FixedTimeProvider(now),
          NullLogger<CapacityCommandExecutor>.Instance);

      var outcome = await executor.ExecuteAsync(
          new SetCapacityCommand(
              Guid.NewGuid(),
              "default",
              7,
              40,
              now.AddMinutes(5)),
          cancellationToken);

      await Assert.That(outcome.Status).IsEqualTo("rejected");
      await Assert.That(outcome.AcceptedGeneration).IsNull();
      await Assert.That(process.LastRequest).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

}
