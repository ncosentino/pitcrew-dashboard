using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal sealed record SupportBrokerRequest(
    string DiagnosticMode,
    string? ProfileId,
    string PackageId);

internal sealed record SupportBrokerResponse(
    JsonElement Report,
    string Markdown);

internal enum SupportBrokerStatus
{
  Succeeded,
  InvalidMode,
  InvalidProfile,
  ScriptMissing,
  ExecutionFailed,
}

internal sealed record SupportBrokerExecution(
    SupportBrokerStatus Status,
    SupportBrokerResponse? Response,
    string? Error);
