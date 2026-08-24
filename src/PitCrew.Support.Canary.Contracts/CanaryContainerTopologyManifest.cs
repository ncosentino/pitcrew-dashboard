namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Describes exact images and resource names for one containerized canary run.
/// </summary>
/// <param name="SchemaVersion">Container topology schema version.</param>
/// <param name="RunId">Canary run identifier.</param>
/// <param name="Dashboard">Dashboard source revision used to build both images.</param>
/// <param name="DashboardImage">Candidate Dashboard image identity.</param>
/// <param name="RelayImage">Candidate relay image identity.</param>
/// <param name="DashboardContainerName">Exact Dashboard container name.</param>
/// <param name="RelayContainerName">Exact relay container name.</param>
/// <param name="DashboardVolumeName">Exact Dashboard data-volume name.</param>
/// <param name="RelayVolumeName">Exact relay data-volume name.</param>
/// <param name="CreatedAt">UTC image-build completion time.</param>
public sealed record CanaryContainerTopologyManifest(
    int SchemaVersion,
    string RunId,
    CanarySourceRevision Dashboard,
    CanaryContainerImageIdentity DashboardImage,
    CanaryContainerImageIdentity RelayImage,
    string DashboardContainerName,
    string RelayContainerName,
    string DashboardVolumeName,
    string RelayVolumeName,
    DateTimeOffset CreatedAt);
