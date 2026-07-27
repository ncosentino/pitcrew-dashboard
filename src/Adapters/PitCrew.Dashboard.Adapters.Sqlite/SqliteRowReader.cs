using System.Globalization;

using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Reads SQLite result columns by name so a projection reorder cannot silently misread a row.
/// </summary>
internal sealed class SqliteRowReader
{
  private readonly SqliteDataReader _reader;
  private readonly Dictionary<string, int> _ordinals;

  public SqliteRowReader(SqliteDataReader reader)
  {
    ArgumentNullException.ThrowIfNull(reader);
    _reader = reader;
    _ordinals = new Dictionary<string, int>(
        reader.FieldCount,
        StringComparer.Ordinal);
    for (var index = 0; index < reader.FieldCount; index++)
    {
      _ordinals[reader.GetName(index)] = index;
    }
  }

  public string String(string column) => _reader.GetString(Ordinal(column));

  public string? OptionalString(string column)
  {
    var ordinal = Ordinal(column);
    return _reader.IsDBNull(ordinal)
        ? null
        : _reader.GetString(ordinal);
  }

  public int Int32(string column) => _reader.GetInt32(Ordinal(column));

  public int? OptionalInt32(string column)
  {
    var ordinal = Ordinal(column);
    return _reader.IsDBNull(ordinal)
        ? null
        : _reader.GetInt32(ordinal);
  }

  public long Int64(string column) => _reader.GetInt64(Ordinal(column));

  public long? OptionalInt64(string column)
  {
    var ordinal = Ordinal(column);
    return _reader.IsDBNull(ordinal)
        ? null
        : _reader.GetInt64(ordinal);
  }

  public double? OptionalDouble(string column)
  {
    var ordinal = Ordinal(column);
    return _reader.IsDBNull(ordinal)
        ? null
        : _reader.GetDouble(ordinal);
  }

  public DateTimeOffset Time(string column) =>
      DateTimeOffset.Parse(
          _reader.GetString(Ordinal(column)),
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

  public DateTimeOffset? OptionalTime(string column)
  {
    var ordinal = Ordinal(column);
    return _reader.IsDBNull(ordinal)
        ? null
        : Time(column);
  }

  private int Ordinal(string column) =>
      _ordinals.TryGetValue(column, out var ordinal)
          ? ordinal
          : throw new InvalidOperationException(
              $"The SQLite projection does not expose a column named '{column}'.");
}
