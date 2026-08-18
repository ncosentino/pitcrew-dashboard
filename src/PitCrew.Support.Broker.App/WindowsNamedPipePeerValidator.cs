using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("windows")]
internal static class WindowsNamedPipePeerValidator
{
  public static bool IsExpectedClient(
      NamedPipeServerStream pipe,
      SecurityIdentifier expectedAgentSid)
  {
    SecurityIdentifier? actualSid = null;
    pipe.RunAsClient(() =>
    {
      using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
      actualSid = identity.User;
    });
    return actualSid is not null && expectedAgentSid.Equals(actualSid);
  }
}
