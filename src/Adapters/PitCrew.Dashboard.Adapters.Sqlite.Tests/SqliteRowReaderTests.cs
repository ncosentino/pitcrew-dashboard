using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteRowReaderTests
{
  [Test]
  public async Task Columns_Are_Read_By_Name_Regardless_Of_Projection_Order(
      CancellationToken cancellationToken)
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            7 AS active_slots,
            NULL AS eligible_slots,
            'running' AS manager_status,
            '2026-07-24T12:00:00.0000000+00:00' AS observed_at;
        """;
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    await Assert.That(await reader.ReadAsync(cancellationToken)).IsTrue();
    var row = new SqliteRowReader(reader);

    await Assert.That(row.Int32("active_slots")).IsEqualTo(7);
    await Assert.That(row.OptionalInt32("eligible_slots")).IsNull();
    await Assert.That(row.String("manager_status")).IsEqualTo("running");
    await Assert.That(row.Time("observed_at")).IsEqualTo(
        new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
  }

  [Test]
  public async Task Reading_An_Absent_Column_Fails_Loudly(
      CancellationToken cancellationToken)
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1 AS active_slots;";
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    await Assert.That(await reader.ReadAsync(cancellationToken)).IsTrue();
    var row = new SqliteRowReader(reader);

    await Assert.That(() => row.Int32("draining_slots"))
        .Throws<InvalidOperationException>();
  }
}
