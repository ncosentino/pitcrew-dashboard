using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Begins SQLite write transactions shared by the fleet and history stores.
/// </summary>
[DoNotAutoRegister]
internal sealed class SqliteFleetTransactionFactory(
    SqliteConnectionFactory _connectionFactory) : IFleetStorageTransactionFactory
{
  public async Task<IFleetStorageTransaction> BeginAsync(
      CancellationToken cancellationToken) =>
      await SqliteFleetTransaction.BeginAsync(
          _connectionFactory,
          cancellationToken);
}
