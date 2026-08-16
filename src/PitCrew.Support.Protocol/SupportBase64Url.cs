namespace PitCrew.Support.Protocol;

/// <summary>
/// Encodes and decodes unpadded URL-safe base64 values used in support envelopes.
/// </summary>
public static class SupportBase64Url
{
  /// <summary>
  /// Encodes bytes as unpadded base64url text.
  /// </summary>
  /// <param name="value">Bytes to encode.</param>
  /// <returns>URL-safe base64 text.</returns>
  public static string Encode(ReadOnlySpan<byte> value) =>
      Convert.ToBase64String(value)
          .TrimEnd('=')
          .Replace('+', '-')
          .Replace('/', '_');

  /// <summary>
  /// Decodes unpadded base64url text.
  /// </summary>
  /// <param name="value">URL-safe base64 text.</param>
  /// <returns>Decoded bytes.</returns>
  public static byte[] Decode(string value)
  {
    var normalized = value.Replace('-', '+').Replace('_', '/');
    var padding = (4 - normalized.Length % 4) % 4;
    return Convert.FromBase64String(normalized + new string('=', padding));
  }
}
