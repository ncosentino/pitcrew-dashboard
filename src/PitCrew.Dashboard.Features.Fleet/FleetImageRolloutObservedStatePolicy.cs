using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class FleetImageRolloutObservedStatePolicy(
    IOptions<FleetDashboardOptions> _options)
    : IImageRolloutObservedStatePolicy
{
  public int ObservedStateMaximumAgeSeconds =>
      _options.Value.ImageRolloutCapabilityFreshnessSeconds;

  public int HistoryPerProfile =>
      _options.Value.ImageRolloutHistoryPerProfile;
}
