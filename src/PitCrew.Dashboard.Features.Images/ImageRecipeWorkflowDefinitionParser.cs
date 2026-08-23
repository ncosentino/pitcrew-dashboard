using System.Text;

using PitCrew.Dashboard.Features.Images.Abstractions;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace PitCrew.Dashboard.Features.Images;

internal static class ImageRecipeWorkflowDefinitionParser
{
  private const string WorkflowDefinitionInvalidCode =
      "github_workflow_definition_invalid";
  private const int MaximumWorkflowUtf8Bytes = 65_536;
  private const int MaximumYamlDepth = 32;
  private const int MaximumYamlNodes = 2_048;
  private const int MaximumYamlCollectionCount = 256;

  public static bool Validate(
      CanonicalImageRecipeRegistration canonical,
      GitHubWorkflowFileContent workflowContent,
      out string? code,
      out string? error)
  {
    if (!ParseRootMapping(
            workflowContent.Content,
            out var root,
            out code,
            out error))
    {
      return false;
    }

    if (!root.TryGetValue(
            "on",
            out var onNode) ||
        onNode is not Dictionary<string, object?> onMap)
    {
      code = WorkflowDefinitionInvalidCode;
      error =
          "The reviewed GitHub workflow file must declare on.workflow_dispatch.";
      return false;
    }

    if (!onMap.TryGetValue(
            "workflow_dispatch",
            out var workflowDispatchNode) ||
        workflowDispatchNode is not Dictionary<string, object?> workflowDispatchMap)
    {
      code = WorkflowDefinitionInvalidCode;
      error =
          "The reviewed GitHub workflow file must declare on.workflow_dispatch.";
      return false;
    }

    if (workflowDispatchMap.Keys.Any(static key =>
            !string.Equals(
                key,
                "inputs",
                StringComparison.Ordinal)))
    {
      code = WorkflowDefinitionInvalidCode;
      error =
          "The reviewed GitHub workflow file must not declare unsupported workflow_dispatch settings.";
      return false;
    }

    if (!workflowDispatchMap.TryGetValue(
            "inputs",
            out var inputsNode) ||
        inputsNode is not Dictionary<string, object?> inputsMap)
    {
      code = WorkflowDefinitionInvalidCode;
      error =
          "The reviewed GitHub workflow file must declare workflow_dispatch inputs.";
      return false;
    }

    return ValidateInputs(
        canonical,
        inputsMap,
        out code,
        out error);
  }

  private static bool ValidateInputs(
      CanonicalImageRecipeRegistration canonical,
      Dictionary<string, object?> inputs,
      out string? code,
      out string? error)
  {
    var seenReserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenCustom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var canonicalInputs = canonical.Inputs.ToDictionary(
        static input => input.Name,
        StringComparer.Ordinal);
    foreach (var pair in inputs)
    {
      switch (pair.Key)
      {
        case "pitcrew_request_id":
        case "pitcrew_source_commit":
        case "pitcrew_recipe_id":
          if (!ValidateReservedInput(
                  pair.Key,
                  pair.Value,
                  out error))
          {
            code = WorkflowDefinitionInvalidCode;
            return false;
          }

          seenReserved.Add(pair.Key);
          continue;
      }

      if (ImageRecipeRegistrationValidation.IsReservedWorkflowInputName(
              pair.Key))
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            $"The reviewed GitHub workflow input '{pair.Key}' must use the exact reserved Dashboard spelling.";
        return false;
      }

      if (ImageRecipeRegistrationValidation.IsSecretShapedInputName(
              pair.Key))
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            $"The reviewed GitHub workflow input '{pair.Key}' appears secret-shaped and is not allowed.";
        return false;
      }

      if (!canonicalInputs.TryGetValue(
              pair.Key,
              out var expectedInput))
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            $"The reviewed GitHub workflow input '{pair.Key}' is not declared by the canonical image recipe schema.";
        return false;
      }

      if (!ValidateCustomInput(
              pair.Key,
              pair.Value,
              expectedInput,
              out error))
      {
        code = WorkflowDefinitionInvalidCode;
        return false;
      }

      seenCustom.Add(pair.Key);
    }

    foreach (var reservedName in new[]
    {
        "pitcrew_request_id",
        "pitcrew_source_commit",
        "pitcrew_recipe_id",
    })
    {
      if (!seenReserved.Contains(reservedName))
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            $"The reviewed GitHub workflow input '{reservedName}' is required.";
        return false;
      }
    }

    foreach (var input in canonical.Inputs)
    {
      if (!seenCustom.Contains(input.Name))
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            $"The reviewed GitHub workflow input '{input.Name}' is required by the canonical image recipe schema.";
        return false;
      }
    }

    code = null;
    error = null;
    return true;
  }

  private static bool ValidateReservedInput(
      string inputName,
      object? node,
      out string? error)
  {
    if (!ReadInputDefinition(
            inputName,
            node,
            out var actualType,
            out var required,
            out var options,
            out error))
    {
      return false;
    }

    if (!required ||
        !string.Equals(
            actualType,
            "string",
            StringComparison.Ordinal) ||
        options.Count > 0)
    {
      error =
          $"The reviewed GitHub workflow input '{inputName}' must be a required string without workflow-defined choices.";
      return false;
    }

    return true;
  }

  private static bool ValidateCustomInput(
      string inputName,
      object? node,
      ImageRecipeInputDefinition expectedInput,
      out string? error)
  {
    if (!ReadInputDefinition(
            inputName,
            node,
            out var actualType,
            out var required,
            out var options,
            out error))
    {
      return false;
    }

    if (required != expectedInput.Required)
    {
      error =
          $"The reviewed GitHub workflow input '{inputName}' must declare required={expectedInput.Required.ToString().ToLowerInvariant()}.";
      return false;
    }

    if (!MatchesExpectedWorkflowType(
            expectedInput,
            actualType,
            options))
    {
      error =
          $"The reviewed GitHub workflow input '{inputName}' does not match the canonical image recipe schema.";
      return false;
    }

    return true;
  }

  private static bool MatchesExpectedWorkflowType(
      ImageRecipeInputDefinition expectedInput,
      string actualType,
      IReadOnlyList<string> options)
  {
    return expectedInput.Type switch
    {
      "string" when expectedInput.AllowedValues is { Count: > 0 } =>
          string.Equals(
              actualType,
              "choice",
              StringComparison.Ordinal) &&
          expectedInput.AllowedValues.SequenceEqual(
              options,
              StringComparer.Ordinal),
      "string" =>
          string.Equals(
              actualType,
              "string",
              StringComparison.Ordinal) &&
          options.Count == 0,
      "boolean" =>
          string.Equals(
              actualType,
              "boolean",
              StringComparison.Ordinal) &&
          options.Count == 0,
      "number" =>
          string.Equals(
              actualType,
              "number",
              StringComparison.Ordinal) &&
          options.Count == 0,
      "integer" =>
          string.Equals(
              actualType,
              "number",
              StringComparison.Ordinal) &&
          options.Count == 0,
      _ => false,
    };
  }

  private static bool ReadInputDefinition(
      string inputName,
      object? node,
      out string actualType,
      out bool required,
      out IReadOnlyList<string> options,
      out string? error)
  {
    actualType = string.Empty;
    required = false;
    options = [];
    error = null;
    if (node is not Dictionary<string, object?> map)
    {
      error =
          $"The reviewed GitHub workflow input '{inputName}' must be a mapping.";
      return false;
    }

    string[] parsedOptions = [];
    var hasType = false;
    foreach (var property in map)
    {
      switch (property.Key)
      {
        case "type":
          if (property.Value is not string typeValue)
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' must declare a scalar type.";
            return false;
          }

          actualType = typeValue;
          if (actualType is not "string" and
              not "boolean" and
              not "number" and
              not "choice")
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' declares unsupported type '{actualType}'.";
            return false;
          }

          hasType = true;
          break;
        case "required":
          if (property.Value is not string requiredValue ||
              !bool.TryParse(
                  requiredValue,
                  out required))
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' must declare required as true or false.";
            return false;
          }

          break;
        case "options":
          if (property.Value is not List<object?> optionNodes)
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' must declare options as a YAML sequence.";
            return false;
          }

          if (optionNodes.Count is 0 or > MaximumYamlCollectionCount)
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' exceeded the supported choice count.";
            return false;
          }

          var parsedValues = new List<string>(optionNodes.Count);
          foreach (var optionNode in optionNodes)
          {
            if (optionNode is not string optionValue ||
                string.IsNullOrWhiteSpace(optionValue) ||
                ImageRecipeRegistrationValidation.IsSecretShapedInputValue(
                    optionValue))
            {
              error =
                  $"The reviewed GitHub workflow input '{inputName}' declares an invalid workflow_dispatch choice.";
              return false;
            }

            parsedValues.Add(optionValue);
          }

          if (parsedValues.Distinct(StringComparer.Ordinal).Count() !=
              parsedValues.Count)
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' declares duplicate workflow_dispatch choices.";
            return false;
          }

          parsedOptions = parsedValues
              .Order(StringComparer.Ordinal)
              .ToArray();
          break;
        case "description":
          if (property.Value is not string)
          {
            error =
                $"The reviewed GitHub workflow input '{inputName}' must declare description as plain text.";
            return false;
          }

          break;
        case "default":
          error =
              $"The reviewed GitHub workflow input '{inputName}' must not declare a default value.";
          return false;
        default:
          error =
              $"The reviewed GitHub workflow input '{inputName}' declares unsupported property '{property.Key}'.";
          return false;
      }
    }

    if (!hasType)
    {
      error =
          $"The reviewed GitHub workflow input '{inputName}' must declare an explicit workflow_dispatch type.";
      return false;
    }

    if (string.Equals(
            actualType,
            "choice",
            StringComparison.Ordinal))
    {
      if (parsedOptions.Length == 0)
      {
        error =
            $"The reviewed GitHub workflow input '{inputName}' must declare non-empty workflow_dispatch choices.";
        return false;
      }
    }
    else if (parsedOptions.Length > 0)
    {
      error =
          $"The reviewed GitHub workflow input '{inputName}' must not declare workflow_dispatch choices for type '{actualType}'.";
      return false;
    }

    options = parsedOptions;
    return true;
  }

  private static bool ParseRootMapping(
      string yaml,
      out Dictionary<string, object?> root,
      out string? code,
      out string? error)
  {
    root = new Dictionary<string, object?>(StringComparer.Ordinal);
    code = null;
    error = null;
    var normalizedYaml = yaml.Length > 0 && yaml[0] == '\uFEFF'
        ? yaml[1..]
        : yaml;
    var byteCount = Encoding.UTF8.GetByteCount(normalizedYaml);
    if (byteCount is <= 0 or > MaximumWorkflowUtf8Bytes)
    {
      code = WorkflowDefinitionInvalidCode;
      error =
          "The reviewed GitHub workflow file exceeded the supported size.";
      return false;
    }

    try
    {
      using var reader = new StringReader(normalizedYaml);
      var parser = new Parser(reader);
      var nodeCount = 0;
      if (!MoveNextNonComment(parser) ||
          parser.Current is not StreamStart)
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            "The reviewed GitHub workflow file is not valid YAML.";
        return false;
      }

      if (!MoveNextNonComment(parser) ||
          parser.Current is not DocumentStart)
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            "The reviewed GitHub workflow file is not valid YAML.";
        return false;
      }

      if (!MoveNextNonComment(parser) ||
          !ReadNode(
              parser,
              0,
              ref nodeCount,
              out var rootNode,
              out error))
      {
        code = WorkflowDefinitionInvalidCode;
        return false;
      }

      if (rootNode is not Dictionary<string, object?> rootMap)
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            "The reviewed GitHub workflow file root must be a mapping.";
        return false;
      }

      if (!MoveNextNonComment(parser) ||
          parser.Current is not DocumentEnd)
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            "The reviewed GitHub workflow file must contain exactly one YAML document.";
        return false;
      }

      if (!MoveNextNonComment(parser) ||
          parser.Current is not StreamEnd ||
          MoveNextNonComment(parser))
      {
        code = WorkflowDefinitionInvalidCode;
        error =
            "The reviewed GitHub workflow file must contain exactly one YAML document.";
        return false;
      }

      root = rootMap;
      return true;
    }
    catch (YamlException)
    {
      code = WorkflowDefinitionInvalidCode;
      error =
          "The reviewed GitHub workflow file is not valid YAML.";
      return false;
    }
  }

  private static bool ReadNode(
      IParser parser,
      int depth,
      ref int nodeCount,
      out object? node,
      out string? error)
  {
    node = null;
    error = null;
    if (depth > MaximumYamlDepth)
    {
      error =
          "The reviewed GitHub workflow file exceeded the supported nesting depth.";
      return false;
    }

    if (parser.Current is null)
    {
      error =
          "The reviewed GitHub workflow file ended before the YAML document completed.";
      return false;
    }

    switch (parser.Current)
    {
      case AnchorAlias:
        error =
            "The reviewed GitHub workflow file must not use anchors, aliases, or tags.";
        return false;
      case Scalar scalar:
        if (!ValidateNodeEvent(
                scalar,
                ref nodeCount,
                out error))
        {
          return false;
        }

        node = scalar.Value;
        return true;
      case MappingStart mappingStart:
        if (!ValidateNodeEvent(
                mappingStart,
                ref nodeCount,
                out error))
        {
          return false;
        }

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        var entryCount = 0;
        while (MoveNextNonComment(parser))
        {
          if (parser.Current is MappingEnd)
          {
            node = map;
            return true;
          }

          if (parser.Current is not Scalar keyScalar)
          {
            error =
                "The reviewed GitHub workflow file must use scalar mapping keys.";
            return false;
          }

          if (!ValidateNodeEvent(
                  keyScalar,
                  ref nodeCount,
                  out error))
          {
            return false;
          }

          var key = keyScalar.Value;
          if (key == "<<")
          {
            error =
                "The reviewed GitHub workflow file must not use anchors, aliases, or tags.";
            return false;
          }

          if (++entryCount > MaximumYamlCollectionCount)
          {
            error =
                "The reviewed GitHub workflow file exceeded the supported mapping size.";
            return false;
          }

          if (!map.TryAdd(
                  key,
                  null))
          {
            error =
                $"The reviewed GitHub workflow file defines duplicate key '{key}'.";
            return false;
          }

          if (!MoveNextNonComment(parser))
          {
            error =
                "The reviewed GitHub workflow file ended before a mapping value was provided.";
            return false;
          }

          if (!ReadNode(
                  parser,
                  depth + 1,
                  ref nodeCount,
                  out var value,
                  out error))
          {
            return false;
          }

          map[key] = value;
        }

        error =
            "The reviewed GitHub workflow file ended before a mapping closed.";
        return false;
      case SequenceStart sequenceStart:
        if (!ValidateNodeEvent(
                sequenceStart,
                ref nodeCount,
                out error))
        {
          return false;
        }

        var list = new List<object?>();
        while (MoveNextNonComment(parser))
        {
          if (parser.Current is SequenceEnd)
          {
            node = list;
            return true;
          }

          if (list.Count >= MaximumYamlCollectionCount)
          {
            error =
                "The reviewed GitHub workflow file exceeded the supported sequence size.";
            return false;
          }

          if (!ReadNode(
                  parser,
                  depth + 1,
                  ref nodeCount,
                  out var item,
                  out error))
          {
            return false;
          }

          list.Add(item);
        }

        error =
            "The reviewed GitHub workflow file ended before a sequence closed.";
        return false;
      default:
        error =
            "The reviewed GitHub workflow file contains unsupported YAML content.";
        return false;
    }
  }

  private static bool ValidateNodeEvent(
      NodeEvent nodeEvent,
      ref int nodeCount,
      out string? error)
  {
    error = null;
    if (!nodeEvent.Anchor.IsEmpty ||
        !nodeEvent.Tag.IsEmpty)
    {
      error =
          "The reviewed GitHub workflow file must not use anchors, aliases, or tags.";
      return false;
    }

    nodeCount++;
    if (nodeCount > MaximumYamlNodes)
    {
      error =
          "The reviewed GitHub workflow file exceeded the supported node budget.";
      return false;
    }

    return true;
  }

  private static bool MoveNextNonComment(IParser parser)
  {
    while (parser.MoveNext())
    {
      if (parser.Current is not Comment)
      {
        return true;
      }
    }

    return false;
  }
}
