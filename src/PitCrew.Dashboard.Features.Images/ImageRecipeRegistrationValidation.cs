using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal static partial class ImageRecipeRegistrationValidation
{
  private const int MaximumAllowedSourceRefs = 16;
  private const int MaximumDeclaredInputCount = 16;
  private const int MaximumAllowedValuesPerInput = 32;
  private const int MaximumInputValueLength = 1024;
  private static readonly FrozenSet<string> _reservedInputNames =
      new[]
      {
          "pitcrew_request_id",
          "pitcrew_source_commit",
          "pitcrew_recipe_id",
      }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
  private static readonly JsonWriterOptions _jsonWriterOptions = new()
  {
    Indented = false,
  };

  public static bool Canonicalize(
      RegisterImageRecipeInput input,
      out CanonicalImageRecipeRegistration? canonical,
      out string? error)
  {
    canonical = null;
    error = null;
    if (input.RegistrationId == Guid.Empty)
    {
      error = "Registration ID must be one non-empty GUID.";
      return false;
    }
    if (!ParsePositiveId(
            input.GitHubInstallationId,
            out var installationId) ||
        !ParsePositiveId(
            input.GitHubRepositoryId,
            out var repositoryId) ||
        !ParsePositiveId(
            input.GitHubWorkflowId,
            out var workflowId))
    {
      error =
          "GitHub installation, repository, and workflow IDs must be positive decimal values.";
      return false;
    }

    var workflowPath = input.WorkflowPath ?? string.Empty;
    if (!IsWorkflowPath(workflowPath))
    {
      error =
          "Workflow path must be an exact .github/workflows/*.yml or .yaml path.";
      return false;
    }

    var dispatchRef = input.DispatchRef ?? string.Empty;
    if (!IsDispatchRef(dispatchRef))
    {
      error =
          "Dispatch ref must be a bounded exact branch or tag and must not be a SHA, URL, or traversal path.";
      return false;
    }

    var recipeId = input.RecipeId ?? string.Empty;
    if (!IsRecipeId(recipeId))
    {
      error =
          "Recipe ID must start with a lowercase letter and contain only lowercase letters, digits, or hyphens.";
      return false;
    }

    if (input.CandidateSchemaVersion != 1)
    {
      error =
          "Candidate schema version must be exactly 1 for image recipe registrations.";
      return false;
    }

    var sourceRefs = input.AllowedSourceRefs
        .Select(static value => value ?? string.Empty)
        .ToArray();
    if (sourceRefs.Length is < 1 or > MaximumAllowedSourceRefs)
    {
      error =
          $"Allowed source refs must contain between 1 and {MaximumAllowedSourceRefs} exact refs.";
      return false;
    }
    if (sourceRefs.Any(static value => !IsAllowedSourceRef(value)) ||
        sourceRefs.Distinct(StringComparer.Ordinal).Count() != sourceRefs.Length)
    {
      error =
          "Allowed source refs must be unique exact refs under refs/heads/ or refs/tags/.";
      return false;
    }
    Array.Sort(
        sourceRefs,
        StringComparer.Ordinal);

    if (input.Inputs.Count > MaximumDeclaredInputCount)
    {
      error =
          $"Image recipe registrations may declare at most {MaximumDeclaredInputCount} workflow inputs.";
      return false;
    }

    var normalizedInputs = new List<ImageRecipeInputDefinition>(
        input.Inputs.Count);
    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var definition in input.Inputs)
    {
      if (definition is null)
      {
        error = "Workflow input definitions must not contain null values.";
        return false;
      }

      var name = definition.Name ?? string.Empty;
      if (!IsInputName(name))
      {
        error =
            "Workflow input names must contain only letters, digits, underscores, or hyphens and be 64 characters or fewer.";
        return false;
      }
      if (_reservedInputNames.Contains(name) ||
          SecretShapedName().IsMatch(name))
      {
        error =
            "Workflow input names must not use reserved Dashboard inputs pitcrew_request_id, pitcrew_source_commit, or pitcrew_recipe_id, or other secret-shaped names.";
        return false;
      }
      if (!seenNames.Add(name))
      {
        error =
            "Workflow input names must be unique within the image recipe registration.";
        return false;
      }

      var type = definition.Type?.ToLowerInvariant() ?? string.Empty;
      if (!IsInputType(type))
      {
        error =
            "Workflow input types must be string, integer, number, or boolean.";
        return false;
      }

      if (type != "string" &&
          (definition.MaxLength is not null ||
              definition.AllowedValues is { Count: > 0 }))
      {
        error =
            "Only string workflow inputs may declare maxLength or allowedValues.";
        return false;
      }

      int? maxLength = null;
      if (definition.MaxLength is not null)
      {
        if (definition.MaxLength.Value is < 1 or > MaximumInputValueLength)
        {
          error =
              $"String workflow input maxLength must be between 1 and {MaximumInputValueLength}.";
          return false;
        }
        maxLength = definition.MaxLength.Value;
      }

      string[]? allowedValues = null;
      if (definition.AllowedValues is { Count: > 0 })
      {
        allowedValues = definition.AllowedValues
            .Select(static value => value ?? string.Empty)
            .ToArray();
        if (allowedValues.Length > MaximumAllowedValuesPerInput ||
            allowedValues.Any(string.IsNullOrWhiteSpace) ||
            allowedValues.Any(value => value.Length > MaximumInputValueLength) ||
            allowedValues.Any(HasControl) ||
            allowedValues.Any(value => SecretShapedValue().IsMatch(value)) ||
            allowedValues.Distinct(StringComparer.Ordinal).Count() !=
            allowedValues.Length)
        {
          error =
              $"String workflow input allowedValues must contain at most {MaximumAllowedValuesPerInput} unique non-secret values.";
          return false;
        }
        if (maxLength is not null &&
            allowedValues.Any(value => value.Length > maxLength.Value))
        {
          error =
              "Every workflow input allowed value must satisfy the declared maxLength.";
          return false;
        }
        Array.Sort(
            allowedValues,
            StringComparer.Ordinal);
      }

      normalizedInputs.Add(new ImageRecipeInputDefinition(
          name,
          type,
          definition.Required,
          maxLength,
          allowedValues));
    }

    normalizedInputs.Sort(
        static (left, right) =>
            StringComparer.Ordinal.Compare(
                left.Name,
                right.Name));
    canonical = new CanonicalImageRecipeRegistration(
        input.RegistrationId,
        installationId,
        repositoryId,
        workflowId,
        workflowPath,
        dispatchRef,
        recipeId,
        1,
        WriteSourceRefPolicyJson(sourceRefs),
        WriteInputSchemaJson(normalizedInputs),
        sourceRefs,
        normalizedInputs);
    return true;
  }

  internal static bool MatchesDurableRegistrationRequest(
      ImageRecipeRegistration registration,
      CanonicalImageRecipeRegistration canonical) =>
      registration.GitHubInstallationId == canonical.GitHubInstallationId &&
      registration.GitHubRepositoryId == canonical.GitHubRepositoryId &&
      registration.GitHubWorkflowId == canonical.GitHubWorkflowId &&
      string.Equals(
          registration.WorkflowPath,
          canonical.WorkflowPath,
          StringComparison.Ordinal) &&
      string.Equals(
          registration.DispatchRef,
          canonical.DispatchRef,
          StringComparison.Ordinal) &&
      string.Equals(
          registration.RecipeId,
          canonical.RecipeId,
          StringComparison.Ordinal) &&
      registration.CandidateSchemaVersion ==
      canonical.CandidateSchemaVersion &&
      string.Equals(
          registration.SourceRefPolicyJson,
          canonical.SourceRefPolicyJson,
          StringComparison.Ordinal) &&
      string.Equals(
          registration.InputSchemaJson,
          canonical.InputSchemaJson,
          StringComparison.Ordinal);

  public static ImageRecipeRegistration CreateRegistration(
      string tenantId,
      int version,
      string createdByGitHubUserId,
      DateTimeOffset createdAt,
      CanonicalImageRecipeRegistration canonical,
      GitHubRepositoryIdentity repository,
      GitHubWorkflowFileRevision revision) =>
      new(
          tenantId,
          canonical.RegistrationId,
          version,
          canonical.GitHubInstallationId,
          canonical.GitHubRepositoryId,
          canonical.GitHubWorkflowId,
          repository.Owner,
          repository.Name,
          canonical.WorkflowPath,
          revision.BlobSha,
          canonical.DispatchRef,
          canonical.RecipeId,
          canonical.CandidateSchemaVersion,
          canonical.SourceRefPolicyJson,
          canonical.InputSchemaJson,
          createdByGitHubUserId,
          createdAt,
          null,
          null);

  public static ImageRecipeRegistrationResponse ToResponse(
      ImageRecipeRegistration registration) =>
      new(
          registration.RegistrationId,
          registration.Version,
          Convert.ToString(
              registration.GitHubInstallationId,
              CultureInfo.InvariantCulture) ??
          throw new InvalidOperationException(
              "GitHub installation ID could not be formatted."),
          Convert.ToString(
              registration.GitHubRepositoryId,
              CultureInfo.InvariantCulture) ??
          throw new InvalidOperationException(
              "GitHub repository ID could not be formatted."),
          Convert.ToString(
              registration.GitHubWorkflowId,
              CultureInfo.InvariantCulture) ??
          throw new InvalidOperationException(
              "GitHub workflow ID could not be formatted."),
          registration.RepositoryOwner,
          registration.RepositoryName,
          registration.WorkflowPath,
          registration.WorkflowBlobSha,
          registration.DispatchRef,
          registration.RecipeId,
          registration.CandidateSchemaVersion,
          ReadAllowedSourceRefs(registration.SourceRefPolicyJson),
          ReadInputs(registration.InputSchemaJson),
          registration.CreatedByGitHubUserId,
          registration.CreatedAt,
          registration.DisabledByGitHubUserId,
          registration.DisabledAt);

  private static string WriteSourceRefPolicyJson(
      IReadOnlyList<string> allowedSourceRefs)
  {
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, _jsonWriterOptions))
    {
      writer.WriteStartObject();
      writer.WritePropertyName("allowedSourceRefs");
      writer.WriteStartArray();
      foreach (var allowedSourceRef in allowedSourceRefs)
      {
        writer.WriteStringValue(allowedSourceRef);
      }
      writer.WriteEndArray();
      writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  private static string WriteInputSchemaJson(
      IReadOnlyList<ImageRecipeInputDefinition> inputs)
  {
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, _jsonWriterOptions))
    {
      writer.WriteStartObject();
      writer.WriteString("type", "object");
      writer.WriteBoolean("additionalProperties", false);
      writer.WritePropertyName("properties");
      writer.WriteStartObject();
      foreach (var input in inputs)
      {
        writer.WritePropertyName(input.Name);
        writer.WriteStartObject();
        writer.WriteString("type", input.Type);
        if (input.MaxLength is not null)
        {
          writer.WriteNumber(
              "maxLength",
              input.MaxLength.Value);
        }
        if (input.AllowedValues is { Count: > 0 })
        {
          writer.WritePropertyName("enum");
          writer.WriteStartArray();
          foreach (var allowedValue in input.AllowedValues)
          {
            writer.WriteStringValue(allowedValue);
          }
          writer.WriteEndArray();
        }
        writer.WriteEndObject();
      }
      writer.WriteEndObject();

      var required = inputs
          .Where(static input => input.Required)
          .Select(static input => input.Name)
          .ToArray();
      if (required.Length > 0)
      {
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        foreach (var propertyName in required)
        {
          writer.WriteStringValue(propertyName);
        }
        writer.WriteEndArray();
      }

      writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  private static IReadOnlyList<string> ReadAllowedSourceRefs(
      string json)
  {
    using var document = JsonDocument.Parse(json);
    if (!(document.RootElement.TryGetProperty(
              "allowedSourceRefs",
              out var allowedRefsElement) ||
          document.RootElement.TryGetProperty(
              "allowedRefs",
              out allowedRefsElement)) ||
        allowedRefsElement.ValueKind != JsonValueKind.Array)
    {
      throw new InvalidOperationException(
          "Stored image recipe source-ref policy is invalid.");
    }

    var allowedRefs = new List<string>();
    using (var items = allowedRefsElement.EnumerateArray())
    {
      while (items.MoveNext())
      {
        var item = items.Current;
        if (item.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(item.GetString()))
        {
          throw new InvalidOperationException(
              "Stored image recipe source-ref policy contains an invalid ref.");
        }

        allowedRefs.Add(item.GetString()!);
      }
    }

    return allowedRefs;
  }

  private static IReadOnlyList<ImageRecipeInputDefinition> ReadInputs(
      string json)
  {
    using var document = JsonDocument.Parse(json);
    if (!document.RootElement.TryGetProperty(
            "properties",
            out var propertiesElement) ||
        propertiesElement.ValueKind != JsonValueKind.Object)
    {
      return [];
    }

    var requiredNames = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    if (document.RootElement.TryGetProperty(
            "required",
            out var requiredElement) &&
        requiredElement.ValueKind == JsonValueKind.Array)
    {
      using var requiredItems = requiredElement.EnumerateArray();
      while (requiredItems.MoveNext())
      {
        var item = requiredItems.Current;
        if (item.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(item.GetString()))
        {
          requiredNames.Add(item.GetString()!);
        }
      }
    }

    var inputs = new List<ImageRecipeInputDefinition>();
    using (var properties = propertiesElement.EnumerateObject())
    {
      while (properties.MoveNext())
      {
        var property = properties.Current;
        if (property.Value.ValueKind != JsonValueKind.Object ||
            !property.Value.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(typeElement.GetString()))
        {
          throw new InvalidOperationException(
              "Stored image recipe input schema is invalid.");
        }

        int? maxLength = null;
        if (property.Value.TryGetProperty(
                "maxLength",
                out var maxLengthElement))
        {
          if (!maxLengthElement.TryGetInt32(out var parsedMaxLength))
          {
            throw new InvalidOperationException(
                "Stored image recipe input schema contains an invalid maxLength.");
          }

          maxLength = parsedMaxLength;
        }

        string[]? allowedValues = null;
        if (property.Value.TryGetProperty("enum", out var enumElement))
        {
          if (enumElement.ValueKind != JsonValueKind.Array)
          {
            throw new InvalidOperationException(
                "Stored image recipe input schema contains an invalid enum definition.");
          }

          var values = new List<string>();
          using var enumValues = enumElement.EnumerateArray();
          while (enumValues.MoveNext())
          {
            var item = enumValues.Current;
            if (item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString()))
            {
              values.Add(item.GetString()!);
            }
          }

          allowedValues = values.Count == 0
              ? null
              : values.ToArray();
        }

        inputs.Add(new ImageRecipeInputDefinition(
            property.Name,
            typeElement.GetString()!,
            requiredNames.Contains(property.Name),
            maxLength,
            allowedValues));
      }
    }

    inputs.Sort(
        static (left, right) =>
            StringComparer.Ordinal.Compare(
                left.Name,
                right.Name));
    return inputs;
  }

  private static bool ParsePositiveId(
      string value,
      out long result)
  {
    result = 0;
    return long.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result) &&
        result > 0;
  }

  private static bool IsRecipeId(string value) =>
      value.Length is >= 1 and <= 64 &&
      value[0] is >= 'a' and <= 'z' &&
      value.All(static character =>
          character is >= 'a' and <= 'z' or
              >= '0' and <= '9' or
              '-');

  private static bool IsAllowedSourceRef(string value) =>
      (value.StartsWith(
           "refs/heads/",
           StringComparison.Ordinal) ||
          value.StartsWith(
              "refs/tags/",
              StringComparison.Ordinal)) &&
      IsGitReferenceText(value);

  private static bool IsDispatchRef(string value)
  {
    if (!IsGitReferenceText(value) ||
        LooksLikeSha1(value) ||
        value.Contains("://", StringComparison.Ordinal))
    {
      return false;
    }

    if (value.StartsWith("refs/", StringComparison.Ordinal))
    {
      return IsAllowedSourceRef(value);
    }

    var segments = value.Split('/');
    if (segments.Length == 0)
    {
      return false;
    }
    return segments.All(static segment =>
        segment.Length > 0 &&
        segment is not "." and not "..");
  }

  private static bool IsGitReferenceText(string value) =>
      !string.IsNullOrWhiteSpace(value) &&
      value.Length <= 255 &&
      !HasControl(value) &&
      !value.Contains(' ') &&
      !value.Contains('\\') &&
      !value.Contains(':') &&
      !value.Contains('~') &&
      !value.Contains('^') &&
      !value.Contains('?') &&
      !value.Contains('*') &&
      !value.Contains('[') &&
      !value.Contains("..", StringComparison.Ordinal) &&
      !value.Contains("@{", StringComparison.Ordinal) &&
      !value.Contains("//", StringComparison.Ordinal) &&
      !value.StartsWith('/') &&
      !value.EndsWith('/') &&
      !value.StartsWith('.') &&
      !value.EndsWith('.') &&
      !value.EndsWith(
          ".lock",
          StringComparison.OrdinalIgnoreCase);

  private static bool IsWorkflowPath(string value)
  {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > 256 ||
        value[0] == '/' ||
        value.Contains('\\') ||
        value.Contains(' ') ||
        HasControl(value))
    {
      return false;
    }

    var segments = value.Split('/');
    if (segments.Length != 3 ||
        segments[0] != ".github" ||
        segments[1] != "workflows" ||
        segments.Any(static segment =>
            segment.Length is 0 or > 100 ||
            segment is "." or ".."))
    {
      return false;
    }

    var fileName = segments[^1];
    return fileName.EndsWith(
               ".yml",
               StringComparison.OrdinalIgnoreCase) ||
           fileName.EndsWith(
               ".yaml",
               StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsSha1(string value) =>
      value.Length == 40 &&
      value.All(static character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static bool LooksLikeSha1(string value) =>
      value.Length == 40 &&
      value.All(static character =>
          character is >= '0' and <= '9' or
              >= 'a' and <= 'f' or
              >= 'A' and <= 'F');

  private static bool IsInputName(string value) =>
      value.Length is >= 1 and <= 64 &&
      value.All(static character =>
          character is >= 'a' and <= 'z' or
              >= 'A' and <= 'Z' or
              >= '0' and <= '9' or
              '_' or
              '-');

  private static bool IsInputType(string value) =>
      value is "string" or "integer" or "number" or "boolean";

  private static bool HasControl(string value) =>
      value.Any(char.IsControl);

  internal static bool IsReservedWorkflowInputName(string value) =>
      _reservedInputNames.Contains(value);

  internal static bool IsSecretShapedInputName(string value) =>
      SecretShapedName().IsMatch(value);

  internal static bool IsSecretShapedInputValue(string value) =>
      SecretShapedValue().IsMatch(value);

  [GeneratedRegex(
      "(?:credential|secret|token|password|passwd|api[_-]?key|private[_-]?key|authorization|cookie|access[_-]?key)",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex SecretShapedName();

  [GeneratedRegex(
      "(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{20,}|sk_(?:live|test)_[A-Za-z0-9]{16,}|eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}|BEGIN [A-Z ]*PRIVATE KEY|bearer\\s+\\S+|authorization\\s*[:=]\\s*\\S+)",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex SecretShapedValue();
}
