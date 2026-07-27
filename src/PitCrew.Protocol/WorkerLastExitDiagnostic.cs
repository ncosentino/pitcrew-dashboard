using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes the manager contract 11 bounded exit evidence captured for one worker identity.
/// </summary>
/// <remarks>
/// A <see langword="null"/> diagnostic means no exit evidence is available and never means a
/// clean exit. Exit code 137 alone never proves an out-of-memory kill because an ordinary
/// <c>SIGKILL</c> produces the same status.
/// </remarks>
/// <param name="ObservedAt">Time the manager captured the exit evidence.</param>
/// <param name="Classification">Exit classification: clean, oom-killed, sigkill, signal, error, launch-failure, or unknown.</param>
/// <param name="ExitCode">Container exit code between 0 and 255, or <see langword="null"/> when unavailable.</param>
/// <param name="Signal">Terminating signal between 1 and 64, or <see langword="null"/> when the worker was not signalled or the signal is unknown.</param>
/// <param name="DockerOomKilled">Docker's out-of-memory flag, or <see langword="null"/> when Docker did not report it.</param>
/// <param name="Evidence">Evidence source: docker-inspect, docker-wait, launch, or unavailable.</param>
public sealed record WorkerLastExitDiagnostic(
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] string Classification,
    [property: JsonRequired] int? ExitCode,
    [property: JsonRequired] int? Signal,
    [property: JsonRequired] bool? DockerOomKilled,
    [property: JsonRequired] string Evidence);
