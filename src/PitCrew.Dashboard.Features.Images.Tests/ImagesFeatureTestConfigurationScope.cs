namespace PitCrew.Dashboard.Features.Images.Tests;

internal sealed class ImagesFeatureTestConfigurationScope : IDisposable
{
  private const string AuthenticationModeKey =
      "PitCrew__Authentication__Mode";
  private const string DataProtectionKeyPathKey =
      "PitCrew__Authentication__DataProtectionKeyPath";
  private const string DatabasePathKey =
      "PitCrew__Sqlite__DatabasePath";
  private const string BusyTimeoutKey =
      "PitCrew__Sqlite__BusyTimeoutMilliseconds";
  private const string ContentionMaximumAttemptsKey =
      "PitCrew__Sqlite__ContentionMaximumAttempts";
  private const string ContentionRetryDelayKey =
      "PitCrew__Sqlite__ContentionRetryDelayMilliseconds";
  private const string EnvironmentKey =
      "ASPNETCORE_ENVIRONMENT";
  private readonly string? _previousAuthenticationMode =
      Environment.GetEnvironmentVariable(AuthenticationModeKey);
  private readonly string? _previousDataProtectionKeyPath =
      Environment.GetEnvironmentVariable(DataProtectionKeyPathKey);
  private readonly string? _previousDatabasePath =
      Environment.GetEnvironmentVariable(DatabasePathKey);
  private readonly string? _previousBusyTimeout =
      Environment.GetEnvironmentVariable(BusyTimeoutKey);
  private readonly string? _previousContentionMaximumAttempts =
      Environment.GetEnvironmentVariable(ContentionMaximumAttemptsKey);
  private readonly string? _previousContentionRetryDelay =
      Environment.GetEnvironmentVariable(ContentionRetryDelayKey);
  private readonly string? _previousEnvironment =
      Environment.GetEnvironmentVariable(EnvironmentKey);
  private readonly string _dataProtectionKeyPath;

  public ImagesFeatureTestConfigurationScope(
      string databasePath,
      int? busyTimeoutMilliseconds = null,
      int? contentionMaximumAttempts = null,
      int? contentionRetryDelayMilliseconds = null)
  {
    _dataProtectionKeyPath = $"{databasePath}.keys";
    Environment.SetEnvironmentVariable(
        AuthenticationModeKey,
        "Development");
    Environment.SetEnvironmentVariable(
        DataProtectionKeyPathKey,
        _dataProtectionKeyPath);
    Environment.SetEnvironmentVariable(
        DatabasePathKey,
        databasePath);
    Environment.SetEnvironmentVariable(
        BusyTimeoutKey,
        busyTimeoutMilliseconds?.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    Environment.SetEnvironmentVariable(
        ContentionMaximumAttemptsKey,
        contentionMaximumAttempts?.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    Environment.SetEnvironmentVariable(
        ContentionRetryDelayKey,
        contentionRetryDelayMilliseconds?.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    Environment.SetEnvironmentVariable(
        EnvironmentKey,
        "Development");
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable(
        AuthenticationModeKey,
        _previousAuthenticationMode);
    Environment.SetEnvironmentVariable(
        DataProtectionKeyPathKey,
        _previousDataProtectionKeyPath);
    Environment.SetEnvironmentVariable(
        DatabasePathKey,
        _previousDatabasePath);
    Environment.SetEnvironmentVariable(
        BusyTimeoutKey,
        _previousBusyTimeout);
    Environment.SetEnvironmentVariable(
        ContentionMaximumAttemptsKey,
        _previousContentionMaximumAttempts);
    Environment.SetEnvironmentVariable(
        ContentionRetryDelayKey,
        _previousContentionRetryDelay);
    Environment.SetEnvironmentVariable(
        EnvironmentKey,
        _previousEnvironment);
    if (Directory.Exists(_dataProtectionKeyPath))
    {
      Directory.Delete(
          _dataProtectionKeyPath,
          true);
    }
  }
}
