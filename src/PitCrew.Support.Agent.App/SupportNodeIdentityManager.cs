namespace PitCrew.Support.Agent.App;

using NexusLabs.Needlr;

/// <summary>
/// Provides explicit local support identity status, disable, and removal operations.
/// </summary>
[DoNotAutoRegister]
public sealed class SupportNodeIdentityManager
{
  private readonly SupportNodeIdentityStore _store;

  internal SupportNodeIdentityManager(SupportNodeIdentityStore store)
  {
    _store = store;
  }

  /// <summary>
  /// Creates the platform-appropriate identity manager for an approved local root.
  /// Windows uses user-scoped persisted CNG keys. Linux uses owner-only PKCS#8 files.
  /// </summary>
  /// <param name="identityRoot">Dedicated absolute identity directory root.</param>
  /// <returns>A local identity manager.</returns>
  /// <exception cref="ArgumentException">
  /// Thrown when <paramref name="identityRoot" /> is empty.
  /// </exception>
  /// <exception cref="PlatformNotSupportedException">
  /// Thrown when the operating system has no supported v1 provider.
  /// </exception>
  public static SupportNodeIdentityManager CreateDefault(string identityRoot)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(identityRoot);
    ISupportNodeKeyProvider provider;
    if (OperatingSystem.IsWindows())
    {
      provider = new WindowsCngSupportNodeKeyProvider();
    }
    else if (OperatingSystem.IsLinux())
    {
      provider = new LinuxFileSupportNodeKeyProvider(new UnixFilePermissions());
    }
    else
    {
      throw new PlatformNotSupportedException(
          "Support identity storage is implemented only for Windows and Linux.");
    }
    return new SupportNodeIdentityManager(
        new SupportNodeIdentityStore(identityRoot, provider));
  }

  /// <summary>
  /// Reads safe identity metadata without returning transport credentials or private key bytes.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels local state reads.</param>
  /// <returns>The current local identity status.</returns>
  public Task<SupportNodeIdentityStatus> GetStatusAsync(
      CancellationToken cancellationToken) =>
      _store.GetStatusAsync(cancellationToken);

  /// <summary>
  /// Disables an active or authorization-rejected identity without deleting its keys.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels the local mutation.</param>
  /// <returns>
  /// <see langword="true" /> when the identity was disabled; otherwise
  /// <see langword="false" />.
  /// </returns>
  public Task<bool> DisableAsync(
      CancellationToken cancellationToken) =>
      _store.DisableAsync(cancellationToken);

  /// <summary>
  /// Removes enrollment state using an explicit private-key preservation choice.
  /// </summary>
  /// <param name="keyChoice">Whether private keys are preserved or deleted.</param>
  /// <param name="cancellationToken">Token that cancels the local mutation.</param>
  /// <returns>
  /// <see langword="true" /> when removal completed; otherwise
  /// <see langword="false" />.
  /// </returns>
  public Task<bool> RemoveAsync(
      SupportIdentityKeyRemovalChoice keyChoice,
      CancellationToken cancellationToken) =>
      _store.RemoveAsync(keyChoice, cancellationToken);

  internal SupportNodeIdentityStore Store => _store;
}
