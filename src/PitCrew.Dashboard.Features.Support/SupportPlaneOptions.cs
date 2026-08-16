using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Features.Support;

/// <summary>
/// Configures Dashboard-owned support-plane authorization and relay integration.
/// </summary>
[Options("PitCrew:SupportPlane", ValidateOnStart = true)]
public sealed class SupportPlaneOptions
{
  /// <summary>
  /// Gets or sets the relay base URL returned to support-agent installers.
  /// </summary>
  [MaxLength(2048)]
  public string RelayUrl { get; set; } = "https://support-relay.example.com";


  /// <summary>
  /// Gets or sets the internal Dashboard-to-relay bearer secret.
  /// Empty disables relay management calls for local development only.
  /// </summary>
  public string RelayInternalBearerSecret { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the Dashboard ECDSA authorization private key as base64url PKCS#8.
  /// Empty uses an ephemeral development key and is not suitable for production.
  /// </summary>
  public string AuthorizationSigningPrivateKeyPkcs8 { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the Dashboard RSA result-decryption private key as base64url PKCS#8.
  /// Empty uses an ephemeral development key and is not suitable for production.
  /// </summary>
  public string ResultDecryptionPrivateKeyPkcs8 { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the one-time enrollment lifetime in seconds.
  /// </summary>
  [Range(300, 86400)]
  public int EnrollmentLifetimeSeconds { get; set; } = 3600;

  /// <summary>
  /// Gets or sets the maximum diagnostic session lifetime in seconds.
  /// </summary>
  [Range(300, 3600)]
  public int MaximumSessionLifetimeSeconds { get; set; } = 900;
}
