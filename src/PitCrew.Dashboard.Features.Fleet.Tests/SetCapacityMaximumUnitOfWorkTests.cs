using System.Globalization;
using System.Security.Claims;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Moq;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SetCapacityMaximumUnitOfWorkTests
{
  private static readonly DateTimeOffset Now = new(
      2026,
      8,
      20,
      2,
      0,
      0,
      TimeSpan.Zero);
  private static readonly Guid NodeId = Guid.Parse(
      "11111111-1111-1111-1111-111111111111",
      CultureInfo.InvariantCulture);
  private static readonly Guid CommandId = Guid.Parse(
      "22222222-2222-2222-2222-222222222222",
      CultureInfo.InvariantCulture);

  private readonly MockRepository _mocks = new(MockBehavior.Strict);

  [Test]
  public async Task Queue_Passes_Claim_Specific_Capability_Freshness_Boundary(
      CancellationToken cancellationToken)
  {
    var principal = new ClaimsPrincipal();
    var user = new AuthenticatedDashboardUser(
        "1",
        "owner",
        "Owner",
        null);
    var options = new FleetDashboardOptions
    {
      CapacityCommandLifetimeMinutes = 7,
      CapacityCapabilityFreshnessSeconds = 90,
    };
    var store = _mocks.Create<ICapacityCommandStore>();
    store
        .Setup(candidate => candidate.QueueAsync(
            "tenant",
            NodeId,
            "default",
            40,
            user.GitHubUserId,
            Now,
            Now.AddMinutes(7),
            Now.AddSeconds(-90),
            cancellationToken,
            null))
        .ReturnsAsync(new CapacityCommandQueueResult(
            CapacityCommandQueueStatus.Queued,
            CommandId));
    var userAccessor =
        _mocks.Create<IAuthenticatedDashboardUserAccessor>();
    userAccessor
        .Setup(candidate => candidate.GetOrNull(principal))
        .Returns(user);
    var unitOfWork = new SetCapacityMaximumUnitOfWork(
        store.Object,
        userAccessor.Object,
        Options.Create(options),
        new FakeTimeProvider(Now));

    var result = await unitOfWork.QueueOrNullAsync(
        principal,
        "tenant",
        NodeId,
        "default",
        40,
        null,
        cancellationToken);

    await Assert.That(result).IsNotNull();
    await Assert.That(result!.Status)
        .IsEqualTo(CapacityCommandQueueStatus.Queued);
    _mocks.VerifyAll();
  }
}
