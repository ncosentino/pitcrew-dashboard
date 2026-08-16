using System.Text.Json;

namespace PitCrew.Dashboard.Features.Support;

internal static class SupportJson
{
  public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
