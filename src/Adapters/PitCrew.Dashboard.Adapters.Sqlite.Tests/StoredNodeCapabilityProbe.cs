using System.Globalization;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

internal sealed record StoredNodeCapability(
    string? Json,
    DateTimeOffset? ReceivedAt);

internal static class StoredNodeCapabilityProbe
{
  internal static async Task<StoredNodeCapability> ReadAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      string family,
      CancellationToken cancellationToken)
  {
    var columns = family switch
    {
      "capacity" => ("capacity_capability_json", "capacity_capability_at"),
      "recovery" => ("recovery_capability_json", "recovery_capability_at"),
      "image-rollout" => (
          "image_rollout_capability_json",
          "image_rollout_capability_at"),
      _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        SELECT {columns.Item1}, {columns.Item2}
        FROM nodes
        WHERE node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      throw new InvalidOperationException($"Node '{nodeId}' was not found.");
    }
    var jsonIsNull = await reader.IsDBNullAsync(0, cancellationToken);
    var receivedAtIsNull = await reader.IsDBNullAsync(1, cancellationToken);
    return new StoredNodeCapability(
        jsonIsNull ? null : reader.GetString(0),
        receivedAtIsNull
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(1),
                CultureInfo.InvariantCulture));
  }
}
