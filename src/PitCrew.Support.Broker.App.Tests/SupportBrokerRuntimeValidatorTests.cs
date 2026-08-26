using System.Security.Principal;

using PitCrew.Support.Broker.App;

namespace PitCrew.Support.Broker.App.Tests;

public sealed class SupportBrokerRuntimeValidatorTests
{
  [Test]
  public async Task Current_Identity_And_Evidence_Are_Ready()
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(
          root,
          "unused");
      var disposition = new SupportBrokerRuntimeValidator(
          options,
          SupportBrokerTestHost.CreateBroker(options))
          .Validate();

      await Assert.That(disposition)
          .IsEqualTo(
              SupportBrokerStartupDispositions.Ready);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Unexpected_Service_Identity_Fails_Closed()
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(
          root,
          "unused");
      options = OperatingSystem.IsWindows()
          ? options with
          {
            BrokerServiceSid =
                new SecurityIdentifier(
                    WellKnownSidType.NullSid,
                    domainSid: null).Value,
          }
          : options with
          {
            BrokerUid = options.BrokerUid + 1,
          };
      var disposition = new SupportBrokerRuntimeValidator(
          options,
          SupportBrokerTestHost.CreateBroker(options))
          .Validate();

      await Assert.That(disposition)
          .IsEqualTo(
              SupportBrokerStartupDispositions
                  .ServiceIdentityUnavailable);
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Runtime_Evidence_Denial_Fails_Startup_Preflight()
  {
    var root = SupportBrokerTestHost.CreatePitCrewRoot();
    try
    {
      var options = SupportBrokerTestHost.CreateOptions(
          root,
          "unused");
      Directory.Delete(
          Path.Combine(
              root,
              ".pitcrew-state",
              "default",
              SupportEvidencePolicy.Load()
                  .ProfileEvidenceDirectory),
          recursive: true);
      var disposition = new SupportBrokerRuntimeValidator(
          options,
          SupportBrokerTestHost.CreateBroker(options))
          .Validate();

      await Assert.That(disposition)
          .IsEqualTo(
              SupportBrokerStartupDispositions
                  .EvidenceAccessDeniedAt(
                      SupportEvidenceFailureStages
                          .EvidenceDirectory));
    }
    finally
    {
      SupportBrokerTestHost.DeleteDirectory(root);
    }
  }
}
