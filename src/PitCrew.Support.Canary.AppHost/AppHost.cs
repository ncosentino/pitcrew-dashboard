using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;

using PitCrew.Support.Canary.AppHost;
using PitCrew.Support.Canary.Contracts;

var runRoot = CanaryAppHostConfiguration.ReadAbsolutePath(
    "PITCREW_CANARY_RUN_ROOT");
var dashboardSourceRoot = CanaryAppHostConfiguration.ReadAbsolutePath(
    "PITCREW_CANARY_DASHBOARD_SOURCE_ROOT");
var configuration = CanaryAppHostConfiguration.ReadConfiguration();
var runId = CanaryAppHostConfiguration.ReadRunId();
var plan = CanaryManifestFile.ReadPlan(
    Path.Combine(runRoot, "plan.json"));
if (!string.Equals(
        plan.RunId,
        runId,
        StringComparison.Ordinal))
{
  throw new InvalidOperationException(
      "Canary AppHost configuration does not match the run plan.");
}
var builder = DistributedApplication.CreateBuilder(args);
var relaySecret = builder.AddParameter(
    "relay-secret",
    secret: true);
var dashboardAuthorizationKey = builder.AddParameter(
    "dashboard-authorization-key",
    secret: true);
var dashboardResultKey = builder.AddParameter(
    "dashboard-result-key",
    secret: true);
builder.Services.AddSingleton(
    new CanaryAppHostOptions(runRoot, runId));
builder.Services.AddHostedService<CanaryStopRequestMonitor>();

var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
    "dotnet";
var runnerDll = CanaryAppHostConfiguration.ResolveCandidateAssembly(
    dashboardSourceRoot,
    "PitCrew.Support.Canary.Runner",
    configuration);
var serviceRoot = Path.Combine(runRoot, "services");
var dashboardStateRoot = Path.Combine(serviceRoot, "dashboard");
var relayStateRoot = Path.Combine(serviceRoot, "relay");
Directory.CreateDirectory(dashboardStateRoot);
Directory.CreateDirectory(relayStateRoot);

if (plan.TopologyProfile == CanaryTopologyProfiles.Containerized)
{
  var topology = CanaryContainerTopologyManifestFile.Read(
      Path.Combine(runRoot, "container-topology.json"));
  if (!string.Equals(
          topology.Dashboard.Commit,
          plan.Dashboard.Commit,
          StringComparison.Ordinal))
  {
    throw new InvalidOperationException(
        "Container topology image identity does not match the run plan.");
  }
  var relay = builder.AddContainer(
          "support-relay",
          topology.RelayImage.Reference)
      .WithImagePullPolicy(ImagePullPolicy.Never)
      .WithContainerName(topology.RelayContainerName)
      .WithContainerNetworkAlias("support-relay-internal")
      .WithHttpEndpoint(
          name: "http",
          targetPort: 8080)
      .WithEnvironment(
          "ASPNETCORE_ENVIRONMENT",
          "Development")
      .WithEnvironment(
          "SupportRelay__DatabasePath",
          "/var/lib/pitcrew-support-relay/support-relay.db")
      .WithEnvironment(
          "SupportRelay__InternalBearerSecret",
          relaySecret)
      .WithVolume(
          topology.RelayVolumeName,
          "/var/lib/pitcrew-support-relay")
      .WithContainerRuntimeArgs(
      [
          "--read-only",
          "--cap-drop=ALL",
          "--security-opt=no-new-privileges:true",
          "--tmpfs=/tmp:size=64m,noexec,nosuid,nodev",
      ])
      .WithHttpHealthCheck("/healthz");
  var dashboard = builder.AddContainer(
          "dashboard",
          topology.DashboardImage.Reference)
      .WithImagePullPolicy(ImagePullPolicy.Never)
      .WithContainerName(topology.DashboardContainerName)
      .WithHttpEndpoint(
          name: "http",
          targetPort: 8080)
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
          "/var/lib/pitcrew-dashboard/pitcrew-dashboard.db")
      .WithEnvironment(
          "PitCrew__Authentication__DataProtectionKeyPath",
          "/var/lib/pitcrew-dashboard/data-protection-keys")
      .WithEnvironment(
          "PitCrew__SupportPlane__RelayUrl",
          relay.GetEndpoint(
              "http",
              KnownNetworkIdentifiers.LocalhostNetwork))
      .WithEnvironment(
          "PitCrew__SupportPlane__RelayInternalUrl",
          "http://support-relay-internal:8080/")
      .WithEnvironment(
          "PitCrew__SupportPlane__RelayInternalBearerSecret",
          relaySecret)
      .WithEnvironment(
          "PitCrew__SupportPlane__AuthorizationSigningPrivateKeyPkcs8",
          dashboardAuthorizationKey)
      .WithEnvironment(
          "PitCrew__SupportPlane__ResultDecryptionPrivateKeyPkcs8",
          dashboardResultKey)
      .WithVolume(
          topology.DashboardVolumeName,
          "/var/lib/pitcrew-dashboard")
      .WithContainerRuntimeArgs(
      [
          "--read-only",
          "--cap-drop=ALL",
          "--security-opt=no-new-privileges:true",
          "--tmpfs=/tmp:size=512m,noexec,nosuid,nodev",
      ])
      .WithHttpHealthCheck("/health")
      .WaitFor(relay);
  AddRuntimeManifest(
      builder,
      dashboard.GetEndpoint(
          "http",
          KnownNetworkIdentifiers.LocalhostNetwork),
      relay.GetEndpoint(
          "http",
          KnownNetworkIdentifiers.LocalhostNetwork),
      dotnet,
      runRoot,
      runnerDll)
      .WaitFor(dashboard)
      .WaitFor(relay);
}
else
{
  var dashboardDll = CanaryAppHostConfiguration.ResolveCandidateAssembly(
      dashboardSourceRoot,
      "PitCrew.Dashboard.WebApi",
      configuration);
  var relayDll = CanaryAppHostConfiguration.ResolveCandidateAssembly(
      dashboardSourceRoot,
      "PitCrew.Support.Relay.App",
      configuration);
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
      .WithEnvironment(
          "PitCrew__SupportPlane__AuthorizationSigningPrivateKeyPkcs8",
          dashboardAuthorizationKey)
      .WithEnvironment(
          "PitCrew__SupportPlane__ResultDecryptionPrivateKeyPkcs8",
          dashboardResultKey)
      .WithHttpHealthCheck("/health")
      .WaitFor(relay);
  AddRuntimeManifest(
      builder,
      dashboard.GetEndpoint(
          "http",
          KnownNetworkIdentifiers.LocalhostNetwork),
      relay.GetEndpoint(
          "http",
          KnownNetworkIdentifiers.LocalhostNetwork),
      dotnet,
      runRoot,
      runnerDll)
      .WaitFor(dashboard)
      .WaitFor(relay);
}

await builder.Build().RunAsync();

static IResourceBuilder<ExecutableResource> AddRuntimeManifest(
    IDistributedApplicationBuilder builder,
    EndpointReference dashboardEndpoint,
    EndpointReference relayEndpoint,
    string dotnet,
    string runRoot,
    string runnerDll)
{
  return builder.AddExecutable(
        "runtime-manifest",
        dotnet,
        runRoot,
        runnerDll,
        "emit-runtime",
        "--run-root",
        runRoot)
    .WithEnvironment(
        "PITCREW_CANARY_DASHBOARD_URL",
        dashboardEndpoint)
    .WithEnvironment(
        "PITCREW_CANARY_RELAY_URL",
        relayEndpoint);
}
