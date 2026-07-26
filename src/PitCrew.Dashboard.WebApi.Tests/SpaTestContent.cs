namespace PitCrew.Dashboard.WebApi.Tests;

internal sealed class SpaTestContent : IDisposable
{
  public SpaTestContent()
  {
    Root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    Directory.CreateDirectory(Root);
    File.WriteAllText(
        Path.Combine(Root, "index.html"),
        "<!doctype html><title>PitCrew SPA test</title>");
  }

  public string Root { get; }

  public void Dispose()
  {
    if (Directory.Exists(Root))
    {
      Directory.Delete(Root, true);
    }
  }
}
