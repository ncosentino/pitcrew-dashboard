using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("windows")]
internal static class WindowsNamedPipePeerValidator
{
  private static readonly Type _claimsIdentityType =
      typeof(ClaimsIdentity);

  public static bool IsExpectedClient(
      NamedPipeServerStream pipe,
      SecurityIdentifier expectedAgentSid)
  {
    SecurityIdentifier? actualSid = null;
    GC.KeepAlive(_claimsIdentityType);
    pipe.RunAsClient(() =>
    {
      using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
      actualSid = identity.User;
    });
    return actualSid is not null && expectedAgentSid.Equals(actualSid);
  }
}
