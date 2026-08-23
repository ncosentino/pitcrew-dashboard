using Microsoft.Extensions.DependencyInjection;

using PitCrew.Support.Canary.AppHost;

var runRoot = CanaryAppHostConfiguration.ReadAbsolutePath(
    "PITCREW_CANARY_RUN_ROOT");
var dashboardSourceRoot = CanaryAppHostConfiguration.ReadAbsolutePath(
    "PITCREW_CANARY_DASHBOARD_SOURCE_ROOT");
var configuration = CanaryAppHostConfiguration.ReadConfiguration();
var relaySecret = CanaryAppHostConfiguration.ReadSecret();
var runId = CanaryAppHostConfiguration.ReadRunId();
var builder = DistributedApplication.CreateBuilder(args);
builder.Services.AddSingleton(
    new CanaryAppHostOptions(runRoot, runId));
builder.Services.AddHostedService<CanaryStopRequestMonitor>();

var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
    "dotnet";
var dashboardDll = CanaryAppHostConfiguration.ResolveCandidateAssembly(
    dashboardSourceRoot,
    "PitCrew.Dashboard.WebApi",
    configuration);
var relayDll = CanaryAppHostConfiguration.ResolveCandidateAssembly(
    dashboardSourceRoot,
    "PitCrew.Support.Relay.App",
    configuration);
var runnerDll = CanaryAppHostConfiguration.ResolveCandidateAssembly(
    dashboardSourceRoot,
    "PitCrew.Support.Canary.Runner",
    configuration);
var serviceRoot = Path.Combine(runRoot, "services");
var dashboardStateRoot = Path.Combine(serviceRoot, "dashboard");
var relayStateRoot = Path.Combine(serviceRoot, "relay");
Directory.CreateDirectory(dashboardStateRoot);
Directory.CreateDirectory(relayStateRoot);

var relay = builder.AddExecutable(
        "support-relay",
        dotnet,
        relayStateRoot,
        relayDll)
    .WithHttpEndpoint(
        name: "http",
        env: "ASPNETCORE_HTTP_PORTS")
    .WithEnvironment(
        "ASPNETCORE_ENVIRONMENT",
        "Development")
    .WithEnvironment(
        "SupportRelay__DatabasePath",
        Path.Combine(relayStateRoot, "support-relay.db"))
    .WithEnvironment(
        "SupportRelay__InternalBearerSecret",
        relaySecret)
    .WithHttpHealthCheck("/healthz");

var dashboard = builder.AddExecutable(
        "dashboard",
        dotnet,
        dashboardStateRoot,
        dashboardDll)
    .WithHttpEndpoint(
        name: "http",
        env: "ASPNETCORE_HTTP_PORTS")
    .WithEnvironment(
        "ASPNETCORE_ENVIRONMENT",
        "Development")
    .WithEnvironment(
        "PitCrew__Authentication__Mode",
        "Development")
    .WithEnvironment(
        "PitCrew__Authentication__DevelopmentGitHubUserId",
        "1")
    .WithEnvironment(
        "PitCrew__Sqlite__DatabasePath",
        Path.Combine(dashboardStateRoot, "pitcrew-dashboard.db"))
    .WithEnvironment(
        "PitCrew__Authentication__DataProtectionKeyPath",
        Path.Combine(dashboardStateRoot, "data-protection-keys"))
    .WithEnvironment(
        "PitCrew__SupportPlane__RelayUrl",
        relay.GetEndpoint("http"))
    .WithEnvironment(
        "PitCrew__SupportPlane__RelayInternalUrl",
        relay.GetEndpoint("http"))
    .WithEnvironment(
        "PitCrew__SupportPlane__RelayInternalBearerSecret",
        relaySecret)
    .WithHttpHealthCheck("/health")
    .WaitFor(relay);

builder.AddExecutable(
        "runtime-manifest",
        dotnet,
        runRoot,
        runnerDll,
        "emit-runtime",
        "--run-root",
        runRoot)
    .WithEnvironment(
        "PITCREW_CANARY_DASHBOARD_URL",
        dashboard.GetEndpoint("http"))
    .WithEnvironment(
        "PITCREW_CANARY_RELAY_URL",
        relay.GetEndpoint("http"))
    .WaitFor(dashboard)
    .WaitFor(relay);

await builder.Build().RunAsync();
