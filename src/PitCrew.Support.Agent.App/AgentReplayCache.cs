using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class AgentReplayCache(string _root)
{
  public void MarkNonce(string nonce)
  {
    Directory.CreateDirectory(NonceRoot);
    var path = Path.Combine(NonceRoot, FileName(nonce));
    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var writer = new StreamWriter(stream);
    writer.Write("seen");
  }

  public bool HasNonce(string nonce) =>
      File.Exists(Path.Combine(NonceRoot, FileName(nonce)));

  public SupportEnvelope? GetResultOrNull(Guid sessionId)
  {
    var path = ResultPath(sessionId);
    if (!File.Exists(path))
    {
      return null;
    }
    return JsonSerializer.Deserialize<SupportEnvelope>(File.ReadAllText(path));
  }

  public void StoreResult(Guid sessionId, SupportEnvelope envelope)
  {
    Directory.CreateDirectory(ResultRoot);
    var target = ResultPath(sessionId);
    var temporary = target + ".new";
    File.WriteAllText(temporary, JsonSerializer.Serialize(envelope));
    File.Move(temporary, target, overwrite: true);
  }

  private string NonceRoot => Path.Combine(_root, "support-nonces");

  private string ResultRoot => Path.Combine(_root, "support-results");

  private string ResultPath(Guid sessionId) =>
      Path.Combine(ResultRoot, sessionId.ToString("N") + ".json");

  private static string FileName(string value)
  {
    var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    return safe + ".tombstone";
  }
}
