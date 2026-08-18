using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PitCrew.Support.Broker.App;

[SupportedOSPlatform("windows")]
internal static class WindowsPipeAccessPolicy
{
  public static PipeSecurity Create(
      SecurityIdentifier agentServiceSid,
      SecurityIdentifier brokerServiceSid)
  {
    var security = new PipeSecurity();
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.AddAccessRule(new PipeAccessRule(
        agentServiceSid,
        PipeAccessRights.ReadWrite,
        AccessControlType.Allow));
    security.AddAccessRule(new PipeAccessRule(
        brokerServiceSid,
        PipeAccessRights.FullControl,
        AccessControlType.Allow));
    security.AddAccessRule(new PipeAccessRule(
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        PipeAccessRights.FullControl,
        AccessControlType.Allow));
    security.AddAccessRule(new PipeAccessRule(
        new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null),
        PipeAccessRights.FullControl,
        AccessControlType.Allow));
    return security;
  }
}
