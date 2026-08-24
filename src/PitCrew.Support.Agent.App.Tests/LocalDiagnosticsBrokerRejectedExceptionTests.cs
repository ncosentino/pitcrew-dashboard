namespace PitCrew.Support.Agent.App.Tests;

public sealed class LocalDiagnosticsBrokerRejectedExceptionTests
{
  [Test]
  public async Task FromStatus_Maps_Only_Closed_Broker_Status()
  {
    var expected = new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
      ["InvalidMode"] = "broker-invalid-mode",
      ["InvalidProfile"] = "broker-invalid-profile",
      ["ScriptMissing"] = "broker-script-missing",
      ["EvidenceAccessDenied"] =
          "broker-evidence-access-denied",
      ["ExecutionFailed"] = "broker-execution-failed",
    };
    var actual = expected.Keys.ToDictionary(
        status => status,
        status =>
            LocalDiagnosticsBrokerRejectedException
                .FromStatus(status)
                .Disposition,
        StringComparer.Ordinal);
    var unknown =
        LocalDiagnosticsBrokerRejectedException.FromStatus(
            "Unexpected");

    await Assert.That(actual).IsEquivalentTo(expected);
    await Assert.That(unknown.Disposition)
        .IsEqualTo("broker-response-invalid");
    await Assert.That(unknown).IsAssignableTo<IOException>();
  }
}
