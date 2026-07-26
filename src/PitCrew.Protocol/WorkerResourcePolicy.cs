namespace PitCrew.Protocol;

/// <summary>
/// Describes the manager contract 11 per-worker resource admission policy.
/// </summary>
/// <remarks>
/// Every limit is independently optional. <see langword="null"/> means the limit is
/// not configured rather than a limit of zero.
/// </remarks>
/// <param name="MemoryBytes">Configured worker memory limit in bytes, or <see langword="null"/> when unlimited.</param>
/// <param name="MemorySwapBytes">Configured worker memory-plus-swap limit in bytes, or <see langword="null"/> when unlimited.</param>
/// <param name="CpuCores">Configured worker CPU limit as an invariant decimal string, or <see langword="null"/> when unlimited.</param>
/// <param name="Pids">Configured worker process-identifier limit, or <see langword="null"/> when unlimited.</param>
public sealed record WorkerResourcePolicy(
    long? MemoryBytes,
    long? MemorySwapBytes,
    string? CpuCores,
    int? Pids);
