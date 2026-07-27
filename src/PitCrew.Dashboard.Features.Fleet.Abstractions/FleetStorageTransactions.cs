namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Represents one durable storage transaction shared by fleet stores.
/// </summary>
/// <remarks>
/// Latest connector state and bounded history are written through the same transaction so a crash,
/// cancellation, or history failure can never advance latest state while losing the sample and the
/// durable manager events that explain it.
/// </remarks>
public interface IFleetStorageTransaction : IAsyncDisposable
{
  /// <summary>
  /// Commits every write enlisted in the transaction.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels the commit.</param>
  /// <returns>A task that completes after the transaction is committed.</returns>
  Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Begins durable storage transactions shared by fleet stores.
/// </summary>
public interface IFleetStorageTransactionFactory
{
  /// <summary>
  /// Begins one durable storage transaction.
  /// </summary>
  /// <remarks>
  /// A transaction that is disposed without a commit rolls back every enlisted write.
  /// </remarks>
  /// <param name="cancellationToken">Token that cancels the begin.</param>
  /// <returns>The started transaction.</returns>
  Task<IFleetStorageTransaction> BeginAsync(CancellationToken cancellationToken);
}
