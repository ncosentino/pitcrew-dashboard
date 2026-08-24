using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal static partial class ImageBuildRequestValidation
{
  private const int MaximumInputCount = 16;
  private const int MaximumInputJsonBytes = 16_384;

  public static bool Canonicalize(
      RequestImageBuildInput input,
      ImageRecipeRegistration registration,
      out string? inputValuesJson,
      out string? inputValuesSha256,
      out string? error)
  {
    inputValuesJson = null;
    inputValuesSha256 = null;
    error = null;
    if (input.RequestId == Guid.Empty
        || input.RegistrationId == Guid.Empty
        || input.RegistrationVersion < 1)
    {
      error =
          "Request ID and registration ID must be non-empty GUIDs and registration version must be positive.";
      return false;
    }
    if (!string.Equals(
            input.SourceCommit,
            input.SourceCommit.ToLowerInvariant(),
            StringComparison.Ordinal)
        || input.SourceCommit.Length != 40
        || input.SourceCommit.Any(static character =>
            !Uri.IsHexDigit(character)))
    {
      error = "Source commit must be an exact lowercase 40-character SHA.";
      return false;
    }

    var allowedRefs = ReadAllowedSourceRefs(registration.SourceRefPolicyJson);
    if (!allowedRefs.Contains(input.SourceRef, StringComparer.Ordinal))
    {
      error = "Source ref must exactly match one frozen allowed source ref.";
      return false;
    }
    if (input.Inputs.Count > MaximumInputCount)
    {
      error = $"Build requests may supply at most {MaximumInputCount} custom inputs.";
      return false;
    }

    using var schema = JsonDocument.Parse(registration.InputSchemaJson);
    var properties = schema.RootElement.GetProperty("properties");
    var required = new List<string>();
    if (schema.RootElement.TryGetProperty(
            "required",
            out var requiredElement))
    {
      using var requiredItems = requiredElement.EnumerateArray();
      while (requiredItems.MoveNext())
      {
        required.Add(requiredItems.Current.GetString()!);
      }
    }
    if (input.Inputs.Keys.Any(static key =>
            ReservedInputName().IsMatch(key)
            || SecretInputName().IsMatch(key))
        || input.Inputs.Keys.Any(key =>
            !properties.TryGetProperty(key, out _)))
    {
      error =
          "Build inputs must contain only declared non-secret custom input names.";
      return false;
    }
    if (required.Any(name => !input.Inputs.ContainsKey(name)))
    {
      error = "Every required custom build input must be supplied.";
      return false;
    }

    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer))
    {
      writer.WriteStartObject();
      foreach (var pair in input.Inputs.OrderBy(
          static pair => pair.Key,
          StringComparer.Ordinal))
      {
        var definition = properties.GetProperty(pair.Key);
        var type = definition.GetProperty("type").GetString();
        writer.WritePropertyName(pair.Key);
        if (!WriteValidatedValue(
                writer,
                pair.Value,
                definition,
                type,
                out error))
        {
          return false;
        }
      }
      writer.WriteEndObject();
    }

    if (buffer.Length > MaximumInputJsonBytes)
    {
      error = "Canonical custom build input values exceed the bounded size.";
      return false;
    }
    inputValuesJson = Encoding.UTF8.GetString(buffer.ToArray());
    inputValuesSha256 = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(inputValuesJson)))
        .ToLowerInvariant();
    return true;
  }

  public static bool MatchesReplay(
      ImageBuildRequest request,
      RequestImageBuildInput input,
      string canonicalInputJson) =>
      request.RegistrationId == input.RegistrationId
      && request.RegistrationVersion == input.RegistrationVersion
      && string.Equals(
          request.SourceRef,
          input.SourceRef,
          StringComparison.Ordinal)
      && string.Equals(
          request.SourceCommit,
          input.SourceCommit,
          StringComparison.Ordinal)
      && string.Equals(
          request.InputValuesJson,
          canonicalInputJson,
          StringComparison.Ordinal);

  public static ImageBuildRequestResponse ToResponse(
      ImageBuildRequest request) =>
      new(
          request.RequestId,
          request.RegistrationId,
          request.RegistrationVersion,
          request.RecipeId,
          request.SourceRepository,
          request.SourceRef,
          request.SourceCommit,
          Format(request.Status),
          request.GitHubRunId?.ToString(CultureInfo.InvariantCulture),
          request.GitHubRunApiUrl,
          request.GitHubRunUrl,
          request.TerminalCategory,
          request.TerminalDetail,
          request.RequestedAt,
          request.UpdatedAt);

  public static IReadOnlyList<string> ReadAllowedSourceRefs(string json)
  {
    using var document = JsonDocument.Parse(json);
    var values = new List<string>();
    using var items = document.RootElement.GetProperty("allowedSourceRefs")
        .EnumerateArray();
    while (items.MoveNext())
    {
      values.Add(items.Current.GetString()!);
    }
    return values;
  }

  public static IReadOnlyDictionary<string, string> ReadDispatchInputs(
      string json)
  {
    using var document = JsonDocument.Parse(json);
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    using var properties = document.RootElement.EnumerateObject();
    while (properties.MoveNext())
    {
      var property = properties.Current;
      values.Add(
        property.Name,
        property.Value.ValueKind switch
        {
          JsonValueKind.String => property.Value.GetString()!,
          JsonValueKind.True => "true",
          JsonValueKind.False => "false",
          _ => property.Value.GetRawText(),
        });
    }
    return values;
  }

  private static bool WriteValidatedValue(
      Utf8JsonWriter writer,
      JsonElement value,
      JsonElement definition,
      string? type,
      out string? error)
  {
    error = null;
    switch (type)
    {
      case "string":
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } stringValue
            || stringValue.Any(char.IsControl)
            || SecretInputValue().IsMatch(stringValue)
            || definition.TryGetProperty("maxLength", out var maxLength)
                && stringValue.Length > maxLength.GetInt32()
            || definition.TryGetProperty("enum", out var allowed)
                && !ContainsAllowedValue(allowed, stringValue))
        {
          error =
              "String build inputs must satisfy the frozen type, length, choice, and secret-value constraints.";
          return false;
        }

        writer.WriteStringValue(stringValue);
        return true;
      case "integer":
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var integerValue))
        {
          error = "Integer build inputs must be exact signed 64-bit integers.";
          return false;
        }
        writer.WriteNumberValue(integerValue);
        return true;
      case "number":
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out var numberValue))
        {
          error = "Number build inputs must be bounded decimal values.";
          return false;
        }
        writer.WriteNumberValue(numberValue);
        return true;
      case "boolean":
        if (value.ValueKind is not JsonValueKind.True
            and not JsonValueKind.False)
        {
          error = "Boolean build inputs must be true or false.";
          return false;
        }
        writer.WriteBooleanValue(value.GetBoolean());
        return true;
      default:
        error = "The frozen build input schema contains an unsupported type.";
        return false;
    }
  }

  private static bool ContainsAllowedValue(
      JsonElement allowed,
      string value)
  {
    using var items = allowed.EnumerateArray();
    while (items.MoveNext())
    {
      if (string.Equals(
          items.Current.GetString(),
          value,
          StringComparison.Ordinal))
      {
        return true;
      }
    }
    return false;
  }

  private static string Format(ImageBuildRequestStatus status) =>
      status.ToString().ToLowerInvariant();

  [GeneratedRegex(
      "^(pitcrew_request_id|pitcrew_source_commit|pitcrew_recipe_id)$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ReservedInputName();

  [GeneratedRegex(
      "(secret|token|password|passwd|credential|private[_-]?key|api[_-]?key)",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex SecretInputName();

  [GeneratedRegex(
      "(gh[pousr]_[A-Za-z0-9]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|Bearer\\s+[A-Za-z0-9._~-]+)",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex SecretInputValue();
}
