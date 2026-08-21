using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Access;

internal sealed class AccessPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    var authenticationOptions = options.Config
        .GetSection("PitCrew:Authentication")
        .Get<DashboardAuthenticationOptions>() ??
        new DashboardAuthenticationOptions();
    var browserAuthenticationScheme =
        authenticationOptions.Mode == DashboardAuthenticationMode.Development
            ? DashboardAuthenticationSchemes.Development
            : DashboardAuthenticationSchemes.Cookie;

    options.Services.TryAddSingleton(TimeProvider.System);
    options.Services.AddSingleton<AccessContextService>();
    options.Services.AddSingleton<
        IAuthorizationHandler,
        TenantAuthorizationHandler>();
    options.Services.AddSingleton<
        IAuthorizationHandler,
        SystemAdministratorAuthorizationHandler>();
    options.Services.AddSingleton<
        IAuthorizationHandler,
        DiagnosticAccessAuthorizationHandler>();
    options.Services.AddSingleton<
        IAuthorizationHandler,
        SupportDiagnosticAccessAuthorizationHandler>();
    options.Services.AddAuthentication()
        .AddScheme<
            AuthenticationSchemeOptions,
            DiagnosticCredentialAuthenticationHandler>(
                DiagnosticAuthenticationDefaults.Scheme,
                static _ => { });
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
                    new TenantAccessRequirement(TenantRole.Owner)))
        .AddPolicy(
            AccessPolicies.DiagnosticsReader,
            policy => policy
                .AddAuthenticationSchemes(
                    DiagnosticAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new DiagnosticAccessRequirement()))
        .AddPolicy(
            AccessPolicies.SupportDiagnosticRequester,
            policy => policy
                .AddAuthenticationSchemes(
                    DiagnosticAuthenticationDefaults.Scheme,
                    browserAuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new SupportDiagnosticAccessRequirement()));
    options.Services.AddHostedService<DevelopmentAccessInitializer>();
  }
}
