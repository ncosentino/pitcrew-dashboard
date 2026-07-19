using PitCrew.Dashboard.Adapters.Sqlite;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
  eventArgs.Cancel = true;
  cancellation.Cancel();
};

var maintenance = new SqliteDatabaseMaintenance();
var result = Execute(
    args,
    maintenance,
    cancellation.Token);
var output = Console.Out;
output.WriteLine(result.Message);
if (!string.IsNullOrWhiteSpace(result.RollbackPath))
{
  output.WriteLine($"Rollback copy: {result.RollbackPath}");
}
return result.Succeeded ? 0 : 1;

static SqliteMaintenanceResult Execute(
    string[] arguments,
    SqliteDatabaseMaintenance maintenance,
    CancellationToken cancellationToken)
{
  if (arguments.Length == 0)
  {
    return Usage();
  }
  var values = ParseArguments(arguments.Skip(1).ToArray());
  return arguments[0].ToLowerInvariant() switch
  {
    "backup" when values.TryGetValue("--database", out var database) &&
        values.TryGetValue("--output", out var output) =>
        maintenance.Backup(
            database,
            output,
            cancellationToken),
    "verify" when values.TryGetValue("--input", out var input) =>
        maintenance.Verify(
            input,
            cancellationToken),
    "restore" when values.TryGetValue("--database", out var destination) &&
        values.TryGetValue("--input", out var backup) =>
        maintenance.Restore(
            backup,
            destination,
            cancellationToken),
    _ => Usage(),
  };
}

static Dictionary<string, string> ParseArguments(string[] arguments)
{
  var values = new Dictionary<string, string>(
      StringComparer.OrdinalIgnoreCase);
  for (var index = 0; index + 1 < arguments.Length; index += 2)
  {
    values[arguments[index]] = arguments[index + 1];
  }
  return values;
}

static SqliteMaintenanceResult Usage() =>
    new(
        false,
        """
        Usage:
          PitCrew.Dashboard.DatabaseTool backup --database <live.db> --output <backup.db>
          PitCrew.Dashboard.DatabaseTool verify --input <backup.db>
          PitCrew.Dashboard.DatabaseTool restore --input <backup.db> --database <stopped-live.db>
        """,
        null);
