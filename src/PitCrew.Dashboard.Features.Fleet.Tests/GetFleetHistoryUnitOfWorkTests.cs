using Microsoft.Extensions.Options;

using Moq;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class GetFleetHistoryUnitOfWorkTests
{
  private static readonly DateTimeOffset Now = new(
      2026,
      7,
      26,
      12,
      0,
      0,
      TimeSpan.Zero);

  private readonly MockRepository _mocks = new(MockBehavior.Strict);

  [Test]
  public async Task Default_Query_Uses_Bounded_Range_And_Point_Limits(
      CancellationToken cancellationToken)
  {
    var options = new FleetDashboardOptions();
    HistoryWindow? captured = null;
    var store = _mocks.Create<IFleetHistoryStore>();
    store
        .Setup(candidate => candidate.GetNodeHistoryAsync(
            "tenant",
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<HistoryWindow>(window => window.To == Now),
            It.Is<DateTimeOffset>(generatedAt => generatedAt == Now),
            It.IsAny<CancellationToken>()))
        .Callback(
            (
                string _,
                Guid _,
                HistoryWindow window,
                DateTimeOffset _,
                CancellationToken _) => captured = window)
        .ReturnsAsync(CreateResponse());

    var result = await CreateUnitOfWork(store, options).GetNodeHistoryAsync(
        "tenant",
        Guid.NewGuid(),
        new HistoryQueryInput(null, null, null, null, null, null),
        cancellationToken);

    await Assert.That(result.Status).IsEqualTo(HistoryQueryStatus.Succeeded);
    await Assert.That(captured).IsNotNull();
    await Assert.That(captured!.From)
        .IsEqualTo(Now.AddHours(-options.DefaultHistoryRangeHours));
    await Assert.That(captured.Resolution)
        .IsEqualTo(HistoryResolution.Raw);
    await Assert.That(captured.PointLimit)
        .IsEqualTo(options.MaximumHistoryPoints);
    await Assert.That(captured.EventLimit)
        .IsEqualTo(options.MaximumHistoryEvents);
  }

  [Test]
  public async Task Query_Rejects_Unbounded_Ranges_And_Limits(
      CancellationToken cancellationToken)
  {
    var options = new FleetDashboardOptions();
    var store = _mocks.Create<IFleetHistoryStore>();
    var unitOfWork = CreateUnitOfWork(store, options);
    var nodeId = Guid.NewGuid();

    var wideRange = await unitOfWork.GetNodeHistoryAsync(
        "tenant",
        nodeId,
        new HistoryQueryInput(
            Now.AddYears(-2).ToString("O"),
            Now.ToString("O"),
            null,
            null,
            null,
            null),
        cancellationToken);
    var invertedRange = await unitOfWork.GetNodeHistoryAsync(
        "tenant",
        nodeId,
        new HistoryQueryInput(
            Now.ToString("O"),
            Now.AddHours(-1).ToString("O"),
            null,
            null,
            null,
            null),
        cancellationToken);
    var unparsableRange = await unitOfWork.GetNodeHistoryAsync(
        "tenant",
        nodeId,
        new HistoryQueryInput("yesterday", null, null, null, null, null),
        cancellationToken);
    var oversizedLimit = await unitOfWork.GetNodeHistoryAsync(
        "tenant",
        nodeId,
        new HistoryQueryInput(
            null,
            null,
            null,
            (options.MaximumHistoryPoints + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            null,
            null),
        cancellationToken);
    var oversizedEvents = await unitOfWork.GetNodeHistoryAsync(
        "tenant",
        nodeId,
        new HistoryQueryInput(
            null,
            null,
            null,
            null,
            (options.MaximumHistoryEvents + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            null),
        cancellationToken);
    var unknownResolution = await unitOfWork.GetNodeHistoryAsync(
        "tenant",
        nodeId,
        new HistoryQueryInput(null, null, "minutely", null, null, null),
        cancellationToken);

    await Assert.That(wideRange.Status).IsEqualTo(HistoryQueryStatus.Invalid);
    await Assert.That(invertedRange.Status)
        .IsEqualTo(HistoryQueryStatus.Invalid);
    await Assert.That(unparsableRange.Status)
        .IsEqualTo(HistoryQueryStatus.Invalid);
    await Assert.That(oversizedLimit.Status)
        .IsEqualTo(HistoryQueryStatus.Invalid);
    await Assert.That(oversizedEvents.Status)
        .IsEqualTo(HistoryQueryStatus.Invalid);
    await Assert.That(unknownResolution.Status)
        .IsEqualTo(HistoryQueryStatus.Invalid);
  }

  [Test]
  public async Task Profile_Query_Rejects_Invalid_Profile_Identifiers(
      CancellationToken cancellationToken)
  {
    var store = _mocks.Create<IFleetHistoryStore>();

    var result = await CreateUnitOfWork(
        store,
        new FleetDashboardOptions()).GetProfileHistoryAsync(
        "tenant",
        Guid.NewGuid(),
        "../escape",
        new HistoryQueryInput(null, null, null, null, null, null),
        cancellationToken);

    await Assert.That(result.Status).IsEqualTo(HistoryQueryStatus.Invalid);
  }

  [Test]
  public async Task Missing_Tenant_Node_Reports_Not_Found(
      CancellationToken cancellationToken)
  {
    var store = _mocks.Create<IFleetHistoryStore>();
    store
        .Setup(candidate => candidate.GetProfileHistoryAsync(
            "tenant",
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            "default",
            It.Is<HistoryWindow>(window => window.To == Now),
            It.Is<DateTimeOffset>(generatedAt => generatedAt == Now),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((NodeHistoryResponse?)null);

    var result = await CreateUnitOfWork(
        store,
        new FleetDashboardOptions()).GetProfileHistoryAsync(
        "tenant",
        Guid.NewGuid(),
        "default",
        new HistoryQueryInput(null, null, null, null, null, null),
        cancellationToken);

    await Assert.That(result.Status).IsEqualTo(HistoryQueryStatus.NotFound);
  }

  [Test]
  public async Task Hourly_Query_Aligns_Bounds_To_Whole_Utc_Hours(
      CancellationToken cancellationToken)
  {
    var options = new FleetDashboardOptions();
    HistoryWindow? captured = null;
    var store = _mocks.Create<IFleetHistoryStore>();
    store
        .Setup(candidate => candidate.GetNodeHistoryAsync(
            "tenant",
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<HistoryWindow>(
                window => window.Resolution == HistoryResolution.Hourly),
            It.Is<DateTimeOffset>(generatedAt => generatedAt == Now),
            It.IsAny<CancellationToken>()))
        .Callback(
            (
                string _,
                Guid _,
                HistoryWindow window,
                DateTimeOffset _,
                CancellationToken _) => captured = window)
        .ReturnsAsync(CreateResponse());

    var result = await CreateUnitOfWork(store, options).GetNodeHistoryAsync(
        "tenant",
        Guid.NewGuid(),
        new HistoryQueryInput(
            Now.AddHours(-6).AddMinutes(20).ToString("O"),
            Now.AddMinutes(-10).ToString("O"),
            "hourly",
            null,
            null,
            null),
        cancellationToken);

    await Assert.That(result.Status).IsEqualTo(HistoryQueryStatus.Succeeded);
    await Assert.That(captured).IsNotNull();
    await Assert.That(captured!.From).IsEqualTo(Now.AddHours(-5));
    await Assert.That(captured.To).IsEqualTo(Now.AddHours(-1));
    await Assert.That(captured.NodePointLimit)
        .IsEqualTo(options.MaximumNodeHistoryPoints);
    await Assert.That(captured.NodeEventLimit)
        .IsEqualTo(options.MaximumNodeHistoryEvents);
  }

  [Test]
  public async Task Hourly_Query_Rejects_Ranges_Without_A_Whole_Hour(
      CancellationToken cancellationToken)
  {
    var store = _mocks.Create<IFleetHistoryStore>();

    var result = await CreateUnitOfWork(store, new FleetDashboardOptions())
        .GetNodeHistoryAsync(
            "tenant",
            Guid.NewGuid(),
            new HistoryQueryInput(
                Now.AddMinutes(-20).ToString("O"),
                Now.AddMinutes(-5).ToString("O"),
                "hourly",
                null,
                null,
                null),
            cancellationToken);

    await Assert.That(result.Status).IsEqualTo(HistoryQueryStatus.Invalid);
    await Assert.That(result.Error).IsNotNull();
  }

  [Test]
  public async Task Capabilities_Advertise_Every_Limit_A_Client_Must_Respect()
  {
    var options = new FleetDashboardOptions();
    var store = _mocks.Create<IFleetHistoryStore>();

    var capabilities = CreateUnitOfWork(store, options).GetCapabilities();

    await Assert.That(capabilities.DefaultRangeHours)
        .IsEqualTo(options.DefaultHistoryRangeHours);
    await Assert.That(capabilities.MaximumRangeHours)
        .IsEqualTo(options.MaximumHistoryRangeHours);
    await Assert.That(capabilities.Resolutions).Contains("raw");
    await Assert.That(capabilities.Resolutions).Contains("hourly");
    await Assert.That(capabilities.MaximumPoints)
        .IsEqualTo(options.MaximumHistoryPoints);
    await Assert.That(capabilities.MaximumEvents)
        .IsEqualTo(options.MaximumHistoryEvents);
    await Assert.That(capabilities.MaximumDiagnostics)
        .IsEqualTo(options.MaximumHistoryDiagnostics);
    await Assert.That(capabilities.NodePointLimit)
        .IsEqualTo(options.MaximumNodeHistoryPoints);
    await Assert.That(capabilities.NodeEventLimit)
        .IsEqualTo(options.MaximumNodeHistoryEvents);
    await Assert.That(capabilities.NodeDiagnosticLimit)
        .IsEqualTo(options.MaximumNodeHistoryDiagnostics);
    await Assert.That(capabilities.ExpectedRawCadenceSeconds)
        .IsEqualTo(options.ConnectorPollSeconds);
    await Assert.That(capabilities.SampleRetentionHours)
        .IsEqualTo(options.TelemetrySampleRetentionDays * 24);
    await Assert.That(capabilities.RollupRetentionHours)
        .IsEqualTo(options.TelemetryRollupRetentionDays * 24);
  }

  private static NodeHistoryResponse CreateResponse() =>
      new(
          Guid.NewGuid(),
          Now,
          Now.AddHours(-24),
          Now,
          "raw",
          [],
          false,
          false,
          false,
          1000,
          200,
          200,
          200,
          5000,
          1000,
          1000,
          []);

  private static GetFleetHistoryUnitOfWork CreateUnitOfWork(
      Mock<IFleetHistoryStore> store,
      FleetDashboardOptions options) =>
      new(
          store.Object,
          Options.Create(options),
          new FixedTimeProvider(Now));

  private sealed class FixedTimeProvider(DateTimeOffset _now) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => _now;
  }
}
