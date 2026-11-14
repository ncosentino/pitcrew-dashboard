using System.Text.RegularExpressions;

namespace PitCrew.Support.Agent.App;

internal static partial class SupportIdentityMigrationReplay
{
  private const int MaximumFiles = 16_384;
  private const int MaximumBytes = 16_777_216;
  private static readonly string[] _directories =
      ["support-nonces", "support-results", "support-rejections"];

  internal static IReadOnlyDictionary<string, string>? Read(string root)
  {
    if (HasLinkedComponent(root))
    {
      return null;
    }
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    long bytes = 0;
    foreach (var name in _directories)
    {
      var directory = Path.Combine(root, name);
      if (!Directory.Exists(directory))
      {
        continue;
      }
      if (HasLinkedComponent(directory) ||
          Directory.EnumerateDirectories(directory).Any())
      {
        return null;
      }
      foreach (var path in Directory.EnumerateFiles(directory))
      {
        var key = name + "/" + Path.GetFileName(path);
        var file = new FileInfo(path);
        bytes += file.Length;
        if (!IsKeyValid(key) || HasLinkedComponent(path) ||
            file.Length > 4_194_304 || bytes > MaximumBytes ||
            result.Count >= MaximumFiles)
        {
          return null;
        }
        result.Add(key, Convert.ToBase64String(File.ReadAllBytes(path)));
      }
    }
    return result;
  }

  internal static bool Write(
      string root,
      IReadOnlyDictionary<string, string> files,
      ISupportNodeKeyProvider provider)
  {
    if (files.Count > MaximumFiles || HasLinkedComponent(root))
    {
      return false;
    }
    var decoded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    long bytes = 0;
    foreach (var (key, value) in files)
    {
      if (!IsKeyValid(key) || value.Length > 5_592_408)
      {
        return false;
      }
      byte[] content;
      try
      {
        content = Convert.FromBase64String(value);
      }
      catch (FormatException)
      {
        return false;
      }
      bytes += content.Length;
      if (content.Length > 4_194_304 || bytes > MaximumBytes)
      {
        return false;
      }
      var parts = key.Split('/');
      var path = Path.Combine(root, parts[0], parts[1]);
      if (HasLinkedComponent(path) ||
          File.Exists(path) &&
          !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
      {
        return false;
      }
      decoded.Add(path, content);
    }
    // No supplied filename becomes a path until every entry has passed the
    // closed filename grammar. Existing state is never overwritten on conflict.
    Directory.CreateDirectory(root);
    provider.SecureDirectory(root);
    foreach (var (path, content) in decoded)
    {
      var directory = Path.GetDirectoryName(path)!;
      Directory.CreateDirectory(directory);
      provider.SecureDirectory(directory);
      if (!File.Exists(path))
      {
        var temporary = path + ".migration-new";
        if (HasLinkedComponent(temporary))
        {
          return false;
        }
        using (var stream = new FileStream(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
          provider.SecureFile(temporary);
          stream.Write(content);
          stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path);
      }
    }
    return true;
  }

  private static bool IsKeyValid(string key) => ReplayFileName().IsMatch(key);

  private static bool HasLinkedComponent(string path)
  {
    var current = new FileInfo(Path.GetFullPath(path));
    if (current.Exists &&
        (current.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      return true;
    }
    for (var directory = current.Directory;
        directory is not null;
        directory = directory.Parent)
    {
      if (directory.Exists &&
          (directory.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        return true;
      }
    }
    return false;
  }

  [GeneratedRegex(
      @"\A(?:support-nonces/[A-F0-9]{64}\.tombstone|support-results/[a-f0-9]{32}\.json|support-rejections/[a-f0-9]{32}\.txt)\z",
      RegexOptions.CultureInvariant,
      1000)]
  private static partial Regex ReplayFileName();
}
