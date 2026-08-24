namespace PitCrew.Support.Agent.App;

using PitCrew.Support.Protocol;

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

  public string Disposition { get; } =
      SupportRequestRejectionDispositions.BrokerResponseInvalid;

  public static LocalDiagnosticsBrokerRejectedException FromStatus(
      string status) =>
      new(
          status switch
          {
            "InvalidMode" =>
                SupportRequestRejectionDispositions.BrokerInvalidMode,
            "InvalidProfile" =>
                SupportRequestRejectionDispositions.BrokerInvalidProfile,
            "ScriptMissing" =>
                SupportRequestRejectionDispositions.BrokerScriptMissing,
            "EvidenceAccessDenied" =>
                SupportRequestRejectionDispositions
                    .BrokerEvidenceAccessDenied,
            "ExecutionFailed" =>
                SupportRequestRejectionDispositions
                    .BrokerExecutionFailed,
            _ => SupportRequestRejectionDispositions
                .BrokerResponseInvalid,
          },
          true);
}
