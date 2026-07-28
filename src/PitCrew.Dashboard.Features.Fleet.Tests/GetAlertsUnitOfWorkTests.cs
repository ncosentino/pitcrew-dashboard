using Microsoft.Extensions.Options;

using Moq;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class GetAlertsUnitOfWorkTests
{
  private static readonly DateTimeOffset Now = new(
      2026,
      7,
      28,
      3,
      0,
      0,
      TimeSpan.Zero);

  private readonly MockRepository _mocks = new(MockBehavior.Strict);

  [Test]
  public async Task Default_Query_Uses_Active_Filter_And_Maximum_Limit(
      CancellationToken cancellationToken)
  {
    var options = new FleetDashboardOptions();
    var store = _mocks.Create<IAlertIncidentStore>();
    store
        .Setup(candidate => candidate.GetAsync(
            "tenant",
            AlertIncidentFilter.Active,
            options.MaximumAlertIncidentsPerQuery,
            Now,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AlertIncidentPage(Now, [], false));

    var result = await CreateUnitOfWork(store, options).GetAsync(
        "tenant",
        new AlertQueryInput(null, null),
        cancellationToken);

    await Assert.That(result.Status).IsEqualTo(AlertQueryStatus.Succeeded);
    await Assert.That(result.Page).IsNotNull();
    _mocks.VerifyAll();
  }

  [Test]
  public async Task Query_Rejects_Unknown_Status_And_Unbounded_Limit(
      CancellationToken cancellationToken)
  {
    var options = new FleetDashboardOptions();
    var store = _mocks.Create<IAlertIncidentStore>();
    var unitOfWork = CreateUnitOfWork(store, options);

    var unknown = await unitOfWork.GetAsync(
        "tenant",
        new AlertQueryInput("pending", null),
        cancellationToken);
    var oversized = await unitOfWork.GetAsync(
        "tenant",
        new AlertQueryInput(
            "all",
            (options.MaximumAlertIncidentsPerQuery + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
        cancellationToken);

    await Assert.That(unknown.Status).IsEqualTo(AlertQueryStatus.Invalid);
    await Assert.That(unknown.Error)
        .IsEqualTo("Status must be 'active', 'resolved', or 'all'.");
    await Assert.That(oversized.Status).IsEqualTo(AlertQueryStatus.Invalid);
  }

  private static GetAlertsUnitOfWork CreateUnitOfWork(
      Mock<IAlertIncidentStore> store,
      FleetDashboardOptions options) =>
      new(
          store.Object,
          Options.Create(options),
          new FixedTimeProvider(Now));

  private sealed class FixedTimeProvider(
      DateTimeOffset _now) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => _now;
  }
}
