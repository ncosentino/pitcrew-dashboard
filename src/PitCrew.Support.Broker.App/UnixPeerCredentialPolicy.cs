namespace PitCrew.Support.Broker.App;

internal static class UnixPeerCredentialPolicy
{
  public static bool IsExpected(
      UnixPeerCredentials credentials,
      uint expectedAgentUid) =>
      credentials.UserId == expectedAgentUid;
}
