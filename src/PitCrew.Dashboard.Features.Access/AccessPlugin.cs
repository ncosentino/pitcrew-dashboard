using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Features.Access;

internal sealed class AccessPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.TryAddSingleton(TimeProvider.System);
    options.Services.AddSingleton<AccessContextService>();
    options.Services.AddSingleton<
        IAuthorizationHandler,
        TenantAuthorizationHandler>();
    options.Services.AddSingleton<
        IAuthorizationHandler,
        SystemAdministratorAuthorizationHandler>();
    options.Services.AddAuthorizationBuilder()
        .AddPolicy(
            AccessPolicies.SystemAdministrator,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new SystemAdministratorRequirement()))
        .AddPolicy(
            AccessPolicies.TenantViewer,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new TenantAccessRequirement(TenantRole.Viewer)))
        .AddPolicy(
            AccessPolicies.TenantAdministrator,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new TenantAccessRequirement(
                        TenantRole.Administrator)))
        .AddPolicy(
            AccessPolicies.TenantOwner,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new TenantAccessRequirement(TenantRole.Owner)));
    options.Services.AddHostedService<DevelopmentAccessInitializer>();
  }
}
