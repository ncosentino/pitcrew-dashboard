using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Loads and expands the sanitized fleet-trust scenario corpus.
/// </summary>
public static class FleetTrustScenarioCorpus
{
  private const string ResourceName =
      "PitCrew.Dashboard.Trust.Scenarios.fleet-trust-scenarios.v1.json";

  private static readonly JsonSerializerOptions _jsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  /// <summary>
  /// Loads the embedded version 1 corpus.
  /// </summary>
  /// <returns>The deterministic scenario set.</returns>
  /// <exception cref="InvalidOperationException">
  /// The build omitted the corpus or the embedded document is invalid.
  /// </exception>
  public static FleetTrustScenarioSet Load()
  {
    var assembly = typeof(FleetTrustScenarioCorpus).Assembly;
    using var stream = assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException(
            $"Embedded scenario corpus '{ResourceName}' is unavailable.");
    return Load(stream, ResourceName);
  }

  internal static FleetTrustScenarioSet Load(
      Stream stream,
      string source)
  {
    ArgumentNullException.ThrowIfNull(stream);
    var boundedSource = Bound(source, 128);
    FleetTrustScenarioSet corpus;
    try
    {
      corpus = JsonSerializer.Deserialize<FleetTrustScenarioSet>(
          stream,
          _jsonOptions) ?? throw new JsonException(
              "The document deserialized to null.");
    }
    catch (JsonException exception)
    {
      var path = string.IsNullOrWhiteSpace(exception.Path)
          ? "$"
          : Bound(exception.Path, 128);
      throw new InvalidOperationException(
          $"Fleet-trust scenario corpus '{boundedSource}' has invalid JSON at '{path}': {Bound(exception.Message, 256)}",
          exception);
    }
    catch (NotSupportedException exception)
    {
      throw new InvalidOperationException(
          $"Fleet-trust scenario corpus '{boundedSource}' uses an unsupported JSON shape: {Bound(exception.Message, 256)}",
          exception);
    }
    catch (IOException exception)
    {
      throw new InvalidOperationException(
          $"Fleet-trust scenario corpus '{boundedSource}' could not be read.",
          exception);
    }

    var semanticError = FleetTrustScenarioValidator.FindError(corpus);
    if (semanticError is not null)
    {
      throw new InvalidOperationException(
          $"Fleet-trust scenario corpus '{boundedSource}' is invalid: {Bound(semanticError, 384)}");
    }
    return corpus;
  }

  /// <summary>
  /// Expands one cardinality case into distinct deterministic incident evidence.
  /// </summary>
  /// <param name="scenario">Cardinality scenario to expand.</param>
  /// <returns>Original one-condition-per-incident decomposition.</returns>
  /// <exception cref="ArgumentNullException">
  /// <paramref name="scenario"/> is <see langword="null"/>.
  /// </exception>
  public static IReadOnlyList<ScenarioIncident> CreateIncidents(
      IncidentCardinalityScenario scenario)
  {
    ArgumentNullException.ThrowIfNull(scenario);
    return Enumerable.Range(1, scenario.IncidentCount)
        .Select(sequence => new ScenarioIncident(
            string.Create(
                CultureInfo.InvariantCulture,
                $"incident-{sequence:D4}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"condition-{sequence:D4}"),
            sequence))
        .ToArray();
  }

  private static string Bound(string? value, int maximumLength)
  {
    var text = string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : value.Trim();
    return text.Length <= maximumLength
        ? text
        : text[..maximumLength];
  }
}
