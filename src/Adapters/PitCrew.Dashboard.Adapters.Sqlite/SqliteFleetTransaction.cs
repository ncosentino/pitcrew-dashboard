using Microsoft.Data.Sqlite;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Carries one SQLite connection and write transaction shared by fleet stores.
/// </summary>
/// <remarks>
/// Latest fleet state and bounded history therefore commit together: a cancelled or failing
/// history append rolls the whole heartbeat back instead of advancing latest state alone.
/// </remarks>
[DoNotAutoRegister]
internal sealed class SqliteFleetTransaction : IFleetStorageTransaction
{
  private SqliteConnection? _connection;
  private SqliteTransaction? _transaction;
  private bool _committed;

  private SqliteFleetTransaction()
  {
  }

  public SqliteConnection Connection => _connection!;

  public SqliteTransaction Transaction => _transaction!;

  /// <summary>
  /// Opens a connection and begins the shared write transaction.
  /// </summary>
  /// <param name="connectionFactory">Factory that opens configured connections.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>The started transaction.</returns>
  public static async Task<SqliteFleetTransaction> BeginAsync(
      SqliteConnectionFactory connectionFactory,
      CancellationToken cancellationToken)
  {
    var enlisted = new SqliteFleetTransaction();
    await enlisted.OpenAsync(connectionFactory, cancellationToken);
    return enlisted;
  }

  /// <summary>
  /// Resolves the SQLite transaction that a store must enlist in.
  /// </summary>
  /// <param name="transaction">Transaction supplied by the caller.</param>
  /// <returns>The SQLite transaction.</returns>
  /// <exception cref="ArgumentException">The transaction was not started by this adapter.</exception>
  public static SqliteFleetTransaction Resolve(
      IFleetStorageTransaction transaction)
  {
    ArgumentNullException.ThrowIfNull(transaction);
    if (transaction is not SqliteFleetTransaction sqlite)
    {
      throw new ArgumentException(
          "The SQLite adapter can only enlist in a SQLite fleet transaction.",
          nameof(transaction));
    }

    return sqlite;
  }

  public async Task CommitAsync(CancellationToken cancellationToken)
  {
    await _transaction!.CommitAsync(cancellationToken);
    _committed = true;
  }

  public async ValueTask DisposeAsync()
  {
    if (_transaction is not null)
    {
      if (!_committed)
      {
        await _transaction.RollbackAsync(CancellationToken.None);
      }

      await _transaction.DisposeAsync();
      _transaction = null;
    }

    if (_connection is not null)
    {
      await _connection.DisposeAsync();
      _connection = null;
    }
  }

  private async Task OpenAsync(
      SqliteConnectionFactory connectionFactory,
      CancellationToken cancellationToken)
  {
    if (_connection is not null)
    {
      await _connection.DisposeAsync();
    }

    _connection = await connectionFactory.OpenAsync(cancellationToken);
    try
    {
      _transaction = (SqliteTransaction)
          await _connection.BeginTransactionAsync(cancellationToken);
    }
    catch
    {
      await DisposeAsync();
      throw;
    }
  }
}
