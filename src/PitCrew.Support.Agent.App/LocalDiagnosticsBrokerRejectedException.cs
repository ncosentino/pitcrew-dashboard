namespace PitCrew.Support.Agent.App;

internal sealed class LocalDiagnosticsBrokerRejectedException :
    IOException
{
  public LocalDiagnosticsBrokerRejectedException()
  {
  }

  public LocalDiagnosticsBrokerRejectedException(
      string? message)
      : base(message)
  {
  }

  public LocalDiagnosticsBrokerRejectedException(
      string? message,
      Exception? innerException)
      : base(message, innerException)
  {
  }

  public LocalDiagnosticsBrokerRejectedException(
      string? message,
      int hresult)
      : base(message, hresult)
  {
  }

  private LocalDiagnosticsBrokerRejectedException(
      string disposition,
      bool _)
      : base("The local support diagnostics broker rejected the request.")
  {
    Disposition = disposition;
  }

  public string Disposition { get; } = "broker-response-invalid";

  public static LocalDiagnosticsBrokerRejectedException FromStatus(
      string status) =>
      new(
          status switch
          {
            "InvalidMode" => "broker-invalid-mode",
            "InvalidProfile" => "broker-invalid-profile",
            "ScriptMissing" => "broker-script-missing",
            "EvidenceAccessDenied" =>
                "broker-evidence-access-denied",
            "ExecutionFailed" => "broker-execution-failed",
            _ => "broker-response-invalid",
          },
          true);
}
