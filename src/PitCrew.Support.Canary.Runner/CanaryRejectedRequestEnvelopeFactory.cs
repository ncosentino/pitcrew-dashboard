using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Canary.Runner;

internal static class CanaryRejectedRequestEnvelopeFactory
{
  private const string TenantId = "local";
  private static readonly Guid _wrongNodeId =
      Guid.Parse(
          "ffffffff-ffff-ffff-ffff-ffffffffffff",
          CultureInfo.InvariantCulture);

  public static SupportEnvelope Create(
      CanaryRejectedRequestControlRequest control,
      ECDsa dashboardSigningKey,
      DateTimeOffset now)
  {
    ArgumentNullException.ThrowIfNull(control);
    ArgumentNullException.ThrowIfNull(dashboardSigningKey);
    if (!CanaryRejectedRequestControlFile.IsEnqueue(control) ||
        control.InjectionCase is null ||
        control.NodeId is null ||
        control.NodeEncryptionPublicKeySpki is null)
    {
      throw new InvalidDataException(
          "A rejected-request envelope requires an enqueue control request.");
    }
    var payload = control.InjectionCase ==
        CanaryRejectedRequestCases.MalformedRequest
        ? Encoding.UTF8.GetBytes("{]")
        : CreateRequestPayload(control, now);
    try
    {
      using var nodeEncryption =
          SupportKeyFactory.ImportRsaPublicKey(
              control.NodeEncryptionPublicKeySpki);
      return SupportEnvelopeCryptography.Seal(
          payload,
          nodeEncryption,
          dashboardSigningKey,
          "dashboard",
          "node");
    }
    finally
    {
      CryptographicOperations.ZeroMemory(payload);
    }
  }

  private static byte[] CreateRequestPayload(
      CanaryRejectedRequestControlRequest control,
      DateTimeOffset now)
  {
    var injectionCase = control.InjectionCase!;
    var expired = injectionCase ==
        CanaryRejectedRequestCases.ExpiredRequest;
    var replay = injectionCase is
        CanaryRejectedRequestCases.ReplaySeed or
        CanaryRejectedRequestCases.RequestReplay;
    var request = new SupportDiagnosticRequest(
        "support-plane-v1",
        injectionCase ==
            CanaryRejectedRequestCases.WrongTenantOrNode
            ? "other-tenant"
            : TenantId,
        injectionCase ==
            CanaryRejectedRequestCases.WrongTenantOrNode
            ? _wrongNodeId
            : control.NodeId!.Value,
        injectionCase ==
            CanaryRejectedRequestCases.SessionMismatch
            ? control.RequestId
            : control.SessionId,
        injectionCase ==
            CanaryRejectedRequestCases.UnsupportedCapability
            ? "unsupported"
            : SupportCapability.DiagnosticsSnapshotV1,
        1,
        injectionCase ==
            CanaryRejectedRequestCases.UnsupportedDiagnosticMode
            ? "Unsupported"
            : SupportDiagnosticModes.ConnectorOffline,
        "default",
        new string('a', 32),
        expired ? now.AddMinutes(-10) : now,
        expired ? now.AddMinutes(-5) : now.AddMinutes(5),
        injectionCase ==
            CanaryRejectedRequestCases.InvalidNonce
            ? "invalid"
            : replay
                ? $"nonce-{control.ReplayId:N}"
                : $"nonce-{control.RequestId:N}");
    return SupportCanonicalJson.SerializeRequest(request);
  }
}
