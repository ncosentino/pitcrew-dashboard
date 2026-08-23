using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Features.Images.Tests;

internal static class ImagesFeatureTestEnvironment
{
  private static readonly string _artifactRoot =
      Path.Combine(
          GetRepositoryRoot(),
          "test-artifacts",
          "images-feature");

  public static string CreateDatabasePath(string scope) =>
      CreatePath(
          _artifactRoot,
          $"pitcrew-image-feature-{scope}-{Guid.NewGuid():N}.db");

  public static void DeleteDatabase(string databasePath)
  {
    SqliteConnection.ClearAllPools();
    foreach (var path in new[]
    {
            databasePath,
            $"{databasePath}-shm",
            $"{databasePath}-wal",
        })
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
  }

  private static string CreatePath(
      string directory,
      string fileName)
  {
    Directory.CreateDirectory(directory);
    return Path.Combine(
        directory,
        fileName);
  }

  private static string GetRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null &&
           !File.Exists(Path.Combine(
               current.FullName,
               "PitCrew.Dashboard.slnx")))
    {
      current = current.Parent;
    }

    return current?.FullName ??
        throw new InvalidOperationException(
            "Could not locate the repository root for image feature tests.");
  }
}
