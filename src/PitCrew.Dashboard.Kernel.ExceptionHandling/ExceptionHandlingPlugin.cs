using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Kernel.ExceptionHandling;

internal sealed class ExceptionHandlingPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.AddExceptionHandler<GlobalExceptionHandler>();
    options.Services.AddProblemDetails();
  }
}
