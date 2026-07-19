using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Access;

[DoNotAutoRegister]
internal sealed class DevelopmentAccessInitializer(
    IAccessStore _accessStore,
    IOptions<DashboardAuthenticationOptions> _options,
    IHostEnvironment _environment,
    TimeProvider _timeProvider) : IHostedLifecycleService
{
  public async Task StartingAsync(CancellationToken cancellationToken)
  {
    await Task.CompletedTask;
  }

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    if (_options.Value.Mode != DashboardAuthenticationMode.Development)
    {
      return;
    }
    if (!_environment.IsDevelopment())
    {
      throw new InvalidOperationException(
          "Development authentication requires the Development host environment.");
    }

    var owner = new DashboardUser(
        _options.Value.DevelopmentGitHubUserId,
        _options.Value.DevelopmentGitHubLogin,
        _options.Value.DevelopmentDisplayName,
        string.IsNullOrWhiteSpace(
            _options.Value.DevelopmentAvatarUrl)
            ? null
            : _options.Value.DevelopmentAvatarUrl);
    await _accessStore.EnsureTenantOwnerAsync(
        "local",
        "Local",
        owner,
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }

  public Task StartedAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StoppingAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StopAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StoppedAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;
}
