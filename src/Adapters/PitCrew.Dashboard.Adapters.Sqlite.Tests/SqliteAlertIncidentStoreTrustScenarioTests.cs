using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Trust.Scenarios;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed partial class SqliteAlertIncidentStoreTests
{
  [Test]
  public async Task Corpus_Over_200_Case_Preserves_Storage_Truncation_Boundary(
      CancellationToken cancellationToken)
  {
    const int sourceCount = 201;
    const int queryLimit = 200;
    var scenario = FleetTrustScenarioCorpus.Load()
        .IncidentCardinalities.Single(
            candidate => candidate.Id == "incidents-over-200");
    var incidents = FleetTrustScenarioCorpus.CreateIncidents(scenario);
    var databasePath = CreateDatabasePath("corpus-over-200");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidates = incidents
          .Select(incident => CreateCandidate(
              incident.ConditionKey,
              "tenant",
              TimeSpan.Zero))
          .ToArray();

      await store.ReconcileAsync(
          candidates,
          [],
          Origin,
          Origin.AddDays(-90),
          10_000,
          cancellationToken);
      var page = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          queryLimit,
          Origin,
          cancellationToken);
      var totalProperties = page.GetType()
          .GetProperties()
          .Where(property => property.Name.Contains(
              "total",
              StringComparison.OrdinalIgnoreCase))
          .Select(property => property.Name)
          .ToArray();

      await Assert.That(scenario.IncidentCount).IsEqualTo(sourceCount);
      await Assert.That(scenario.QueryLimit).IsEqualTo(queryLimit);
      await Assert.That(incidents.Count).IsEqualTo(sourceCount);
      await Assert.That(incidents.Select(incident => incident.ConditionKey)
          .Distinct(StringComparer.Ordinal)
          .Count())
          .IsEqualTo(sourceCount);
      await Assert.That(page.Incidents.Count).IsEqualTo(queryLimit);
      await Assert.That(page.Truncated).IsTrue()
          .Because("one independently keyed incident is outside the bounded page");
      await Assert.That(totalProperties).Contains("TotalCount");
      await Assert.That(page.TotalCount).IsEqualTo(sourceCount);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }
}
