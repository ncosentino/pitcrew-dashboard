using System.Net;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Configures the trusted reverse proxies allowed to supply forwarded request metadata.
/// </summary>
[Options("PitCrew:ReverseProxy", ValidateOnStart = true)]
public sealed class DashboardReverseProxyOptions
{
  /// <summary>
  /// Gets or sets the exact proxy IP addresses trusted for one forwarded hop.
  /// </summary>
  public string[] KnownProxyAddresses { get; set; } = [];

  /// <summary>
  /// Validates configured proxy addresses.
  /// </summary>
  /// <returns>Configuration validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    foreach (var address in KnownProxyAddresses)
    {
      if (!IPAddress.TryParse(address, out _))
      {
        yield return $"Known proxy address '{address}' is not a valid IP address.";
      }
    }
  }
}
