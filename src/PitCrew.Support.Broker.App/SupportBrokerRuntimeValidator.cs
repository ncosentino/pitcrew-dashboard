using System.Security.Principal;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerRuntimeValidator(
    SupportBrokerOptions _options,
    SupportDiagnosticsBroker _broker)
{
  public string Validate()
  {
    if (!HasExpectedServiceIdentity())
    {
      return SupportBrokerStartupDispositions
          .ServiceIdentityUnavailable;
    }
    foreach (var profileId in _options.AllowedProfiles)
    {
      var evidence = _broker.ValidateEvidence(profileId);
      if (evidence.Status == SupportBrokerStatus.Succeeded)
      {
        continue;
      }
      return evidence.Status switch
      {
        SupportBrokerStatus.InvalidProfile =>
            SupportBrokerStartupDispositions
                .EvidenceProfileInvalid,
        SupportBrokerStatus.ScriptMissing =>
            SupportBrokerStartupDispositions
                .EvidenceScriptMissing,
        SupportBrokerStatus.EvidenceAccessDenied =>
            SupportBrokerStartupDispositions
                .EvidenceAccessDenied,
        _ => SupportBrokerStartupDispositions
            .EvidenceInstallationInvalid,
      };
    }
    return SupportBrokerStartupDispositions.Ready;
  }

  private bool HasExpectedServiceIdentity()
  {
    if (OperatingSystem.IsWindows())
    {
      var brokerServiceSid = _options.BrokerServiceSid;
      if (string.IsNullOrWhiteSpace(brokerServiceSid))
      {
        return false;
      }
      SecurityIdentifier expectedSid;
      try
      {
        expectedSid = new SecurityIdentifier(
            brokerServiceSid);
      }
      catch (ArgumentException)
      {
        return false;
      }
      using var identity = WindowsIdentity.GetCurrent(
          TokenAccessLevels.Query);
      return identity.User is { } user &&
          expectedSid.Equals(user) ||
          (identity.Groups?.Contains(expectedSid) ?? false);
    }
    return !OperatingSystem.IsLinux() ||
        _options.BrokerUid ==
            UnixProcessIdentity.GetEffectiveUserId();
  }
}
