using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class AgentReplayCache(string _root)
{
  public bool ClaimNonce(string nonce)
  {
    Directory.CreateDirectory(NonceRoot);
    var path = Path.Combine(NonceRoot, FileName(nonce));
    try
    {
      using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      using var writer = new StreamWriter(stream);
      writer.Write("seen");
      return true;
    }
    catch (IOException) when (File.Exists(path))
    {
      return false;
    }
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
    try
    {
      var item = new FileInfo(path);
      return item.Length is > 0 and <= 4_194_304
          ? JsonSerializer.Deserialize<SupportEnvelope>(File.ReadAllText(path))
          : null;
    }
    catch (JsonException)
    {
      return null;
    }
    catch (IOException)
    {
      return null;
    }
  }

  public string? GetRejectionOrNull(Guid sessionId)
  {
    var path = RejectionPath(sessionId);
    if (!File.Exists(path))
    {
      return null;
    }
    try
    {
      var item = new FileInfo(path);
      if (item.Length is <= 0 or > 128)
      {
        return null;
      }
      var disposition = File.ReadAllText(path).Trim();
      return SupportRequestRejectionDispositions.IsSupported(
          disposition)
          ? disposition
          : null;
    }
    catch (IOException)
    {
      return null;
    }
  }

  public void StoreRejection(
      Guid sessionId,
      string disposition)
  {
    if (!SupportRequestRejectionDispositions.IsSupported(
        disposition))
    {
      throw new ArgumentOutOfRangeException(
          nameof(disposition));
    }
    Directory.CreateDirectory(RejectionRoot);
    var target = RejectionPath(sessionId);
    var temporary = target + ".new";
    File.WriteAllText(temporary, disposition + "\n");
    File.Move(temporary, target, overwrite: true);
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

  private string RejectionRoot =>
      Path.Combine(_root, "support-rejections");

  private string ResultPath(Guid sessionId) =>
      Path.Combine(ResultRoot, sessionId.ToString("N") + ".json");

  private string RejectionPath(Guid sessionId) =>
      Path.Combine(
          RejectionRoot,
          sessionId.ToString("N") + ".txt");

  private static string FileName(string value)
  {
    var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    return safe + ".tombstone";
  }
}
