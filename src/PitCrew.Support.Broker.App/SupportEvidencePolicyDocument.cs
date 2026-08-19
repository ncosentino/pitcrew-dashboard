namespace PitCrew.Support.Broker.App;

internal sealed record SupportEvidencePolicyDocument(
    int SchemaVersion,
    string PitCrewVersion,
    string PitCrewCommit,
    string CollectorRelativePath,
    string CollectorSha256,
    string CollectorHashCanonicalization,
    string ProfileStateRootAccess,
    string ProfileEvidenceDirectory,
    string WindowsEvidenceInheritance,
    string LinuxEvidenceInheritance,
    IReadOnlyList<string> InstallationSentinels,
    IReadOnlyList<string> ProfileProjectionFiles,
    IReadOnlyList<string> ConnectorHealthFiles);
