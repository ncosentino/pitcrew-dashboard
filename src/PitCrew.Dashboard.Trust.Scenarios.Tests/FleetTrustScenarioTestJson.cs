using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PitCrew.Dashboard.Trust.Scenarios.Tests;

internal static class FleetTrustScenarioTestJson
{
  private static readonly JsonSerializerOptions _options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  internal static string Mutate(Action<JsonObject> mutation)
  {
    var root = JsonSerializer.SerializeToNode(
        FleetTrustScenarioCorpus.Load(),
        _options)?.AsObject() ?? throw new InvalidOperationException(
            "The canonical scenario corpus did not serialize as an object.");
    mutation(root);
    return root.ToJsonString(_options);
  }

  internal static FleetTrustScenarioSet Load(string json)
  {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    return FleetTrustScenarioCorpus.Load(stream, "negative-test");
  }
}
