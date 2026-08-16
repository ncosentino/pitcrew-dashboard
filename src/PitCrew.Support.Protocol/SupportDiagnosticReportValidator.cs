using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PitCrew.Support.Protocol;

/// <summary>
/// Validates the bounded file-only diagnostic report before a node signs it or
/// Dashboard stores it.
/// </summary>
public static partial class SupportDiagnosticReportValidator
{
  private static readonly FrozenSet<string> _allowedTopLevelProperties =
      new[]
      {
          "schemaVersion",
          "collectorVersion",
          "collectorSha256",
          "packageId",
          "diagnosticMode",
          "collectionScope",
          "platform",
          "platformSource",
          "profile",
          "pitcrewRoot",
          "startedAt",
          "completedAt",
          "verifiedMeasurements",
          "unavailableEvidence",
          "hypotheses",
      }.ToFrozenSet(StringComparer.Ordinal);

  /// <summary>
  /// Verifies report identity, redaction, shape, and recursive text bounds.
  /// </summary>
  /// <param name="report">Report JSON returned by the local diagnostics broker.</param>
  /// <param name="diagnosticMode">Mode authorized by Dashboard.</param>
  /// <param name="profileId">Optional profile explicitly authorized by Dashboard.</param>
  /// <param name="packageId">Package identifier bound to the signed request.</param>
  /// <returns><see langword="true"/> only for the support-plane v1 file-only report contract.</returns>
  public static bool IsValid(
      JsonElement report,
      string diagnosticMode,
      string? profileId,
      string packageId)
  {
    try
    {
      return IsValidCore(
          report,
          diagnosticMode,
          profileId,
          packageId);
    }
    catch (RegexMatchTimeoutException)
    {
      return false;
    }
  }

  private static bool IsValidCore(
      JsonElement report,
      string diagnosticMode,
      string? profileId,
      string packageId)
  {
    if (report.ValueKind != JsonValueKind.Object ||
        report.GetRawText().Length > 2_097_152 ||
        !HasOnlyAllowedTopLevelProperties(report) ||
        !ReadInt32(report, "schemaVersion", out var schemaVersion) ||
        schemaVersion != 1 ||
        !ReadString(report, "collectionScope", out var collectionScope) ||
        !string.Equals(collectionScope, "file-only", StringComparison.Ordinal) ||
        !ReadString(report, "diagnosticMode", out var reportMode) ||
        !string.Equals(reportMode, diagnosticMode, StringComparison.Ordinal) ||
        !ReadString(report, "pitcrewRoot", out var pitCrewRoot) ||
        !string.Equals(pitCrewRoot, "<pitcrew-root>", StringComparison.Ordinal) ||
        !ReadString(report, "packageId", out var reportPackageId) ||
        !string.Equals(reportPackageId, packageId, StringComparison.Ordinal) ||
        !ReadString(report, "collectorSha256", out var collectorSha256) ||
        !IsLowercaseHex(collectorSha256, 64) ||
        !ReadString(report, "profile", out var reportProfile) ||
        !ProfileId().IsMatch(reportProfile) ||
        profileId is not null &&
        !string.Equals(reportProfile, profileId, StringComparison.Ordinal))
    {
      return false;
    }

    var visited = 0;
    return ValidateElement(report, 0, ref visited);
  }

  /// <summary>
  /// Verifies that diagnostic markdown is bounded and contains no private host
  /// path or credential-shaped value.
  /// </summary>
  /// <param name="markdown">Markdown produced from the validated report.</param>
  /// <returns><see langword="true"/> when the markdown is safe to sign and store.</returns>
  public static bool IsSafeMarkdown(string? markdown)
  {
    if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > 1_048_576)
    {
      return false;
    }
    try
    {
      return !UnsafeText().IsMatch(markdown) &&
          !OpaqueCredential().IsMatch(markdown) &&
          !PrivatePath().IsMatch(markdown) &&
          !PrivateNetworkLocation().IsMatch(markdown) &&
          !UrlWithQuery().IsMatch(markdown);
    }
    catch (RegexMatchTimeoutException)
    {
      return false;
    }
  }

  private static bool HasOnlyAllowedTopLevelProperties(JsonElement report)
  {
    using var properties = report.EnumerateObject();
    while (properties.MoveNext())
    {
      var property = properties.Current;
      if (!_allowedTopLevelProperties.Contains(property.Name))
      {
        return false;
      }
    }
    return true;
  }

  private static bool ValidateElement(
      JsonElement value,
      int depth,
      ref int visited)
  {
    if (depth > 64 || ++visited > 16_384)
    {
      return false;
    }
    switch (value.ValueKind)
    {
      case JsonValueKind.Object:
        var propertyCount = 0;
        using (var properties = value.EnumerateObject())
        {
          while (properties.MoveNext())
          {
            var property = properties.Current;
            if (++propertyCount > 512 ||
                property.Name.Length > 128 ||
                ForbiddenPropertyName().IsMatch(property.Name) ||
                !ValidateElement(property.Value, depth + 1, ref visited))
            {
              return false;
            }
          }
        }
        return true;
      case JsonValueKind.Array:
        var itemCount = 0;
        using (var items = value.EnumerateArray())
        {
          while (items.MoveNext())
          {
            if (++itemCount > 1_024 ||
                !ValidateElement(items.Current, depth + 1, ref visited))
            {
              return false;
            }
          }
        }
        return true;
      case JsonValueKind.String:
        var text = value.GetString() ?? string.Empty;
        return text.Length <= 4_096 &&
            !UnsafeText().IsMatch(text) &&
            !OpaqueCredential().IsMatch(text) &&
            !PrivatePath().IsMatch(text) &&
            !PrivateNetworkLocation().IsMatch(text) &&
            !UrlWithQuery().IsMatch(text);
      case JsonValueKind.Number:
      case JsonValueKind.True:
      case JsonValueKind.False:
      case JsonValueKind.Null:
        return true;
      default:
        return false;
    }
  }

  private static bool ReadString(
      JsonElement value,
      string propertyName,
      out string result)
  {
    result = string.Empty;
    if (!value.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String)
    {
      return false;
    }
    result = property.GetString() ?? string.Empty;
    return !string.IsNullOrWhiteSpace(result);
  }

  private static bool ReadInt32(
      JsonElement value,
      string propertyName,
      out int result)
  {
    result = 0;
    return value.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt32(out result);
  }

  private static bool IsLowercaseHex(string value, int length) =>
      value.Length == length &&
      value.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  [GeneratedRegex(
      "(credential|secret|token|password|passwd|apikey|api_key|privatekey|authorization|cookie|environment|jit|registrationpayload|joboutput|connectoridentity|^(?:value|content|raw|payload|body|data)$)",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex ForbiddenPropertyName();

  [GeneratedRegex(
      "((?:password|passwd|secret|api[_-]?key|access[_-]?token)\\s*[:=]\\s*\\S+|bearer\\s+\\S+|authorization\\s*[:=]\\s*\\S+|BEGIN [A-Z ]*PRIVATE KEY|[\\u0000-\\u0008\\u000B\\u000C\\u000E-\\u001F\\u007F])",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex UnsafeText();

  [GeneratedRegex(
      "(?:[A-Z]:[\\\\/]|\\\\\\\\[^\\\\\\s]+\\\\|(?<![A-Za-z0-9:])/(?:etc|root|home|Users|var|tmp|opt|srv|run|mnt|media|proc|sys|dev)(?:/|\\b))",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex PrivatePath();

  [GeneratedRegex(
      "(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|pcs_(?:node|enroll)_[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{20,}|sk_(?:live|test)_[A-Za-z0-9]{16,}|eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,})",
      RegexOptions.CultureInvariant,
      100)]
  private static partial Regex OpaqueCredential();

  [GeneratedRegex(
      "(?:https?://)?(?:localhost|127(?:\\.\\d{1,3}){3}|10(?:\\.\\d{1,3}){3}|192\\.168(?:\\.\\d{1,3}){2}|172\\.(?:1[6-9]|2\\d|3[01])(?:\\.\\d{1,3}){2}|[A-Za-z0-9.-]+\\.(?:internal|local|lan))(?::\\d+)?(?:/\\S*)?",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex PrivateNetworkLocation();

  [GeneratedRegex(
      "https?://\\S+\\?",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
      100)]
  private static partial Regex UrlWithQuery();

  [GeneratedRegex(
      "^[a-z0-9][a-z0-9-]{0,31}$",
      RegexOptions.CultureInvariant,
      100)]
  private static partial Regex ProfileId();
}
