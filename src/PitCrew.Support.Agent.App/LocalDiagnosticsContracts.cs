using System.Text.Json;

namespace PitCrew.Support.Agent.App;

internal sealed record LocalDiagnosticsRequest(
    string DiagnosticMode,
    string? ProfileId,
    string PackageId);

internal sealed record LocalDiagnosticsResult(
    JsonElement Report,
    string Markdown);

internal interface ILocalDiagnosticsBroker
{
  Task<LocalDiagnosticsResult> ExecuteAsync(
      LocalDiagnosticsRequest request,
      CancellationToken cancellationToken);
}
