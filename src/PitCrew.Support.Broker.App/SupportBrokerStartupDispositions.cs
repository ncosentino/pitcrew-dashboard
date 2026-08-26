namespace PitCrew.Support.Broker.App;

internal static class SupportBrokerStartupDispositions
{
  public const string Ready = "ready";
  public const string ServiceIdentityUnavailable =
      "service-identity-unavailable";
  public const string EvidenceProfileInvalid =
      "evidence-profile-invalid";
  public const string EvidenceScriptMissing =
      "evidence-script-missing";
  public const string EvidenceAccessDenied =
      "evidence-access-denied";
  public const string EvidenceInstallationInvalid =
      "evidence-installation-invalid";

  public static string EvidenceAccessDeniedAt(
      string failureStage) =>
      $"{EvidenceAccessDenied}-{failureStage}";
}
