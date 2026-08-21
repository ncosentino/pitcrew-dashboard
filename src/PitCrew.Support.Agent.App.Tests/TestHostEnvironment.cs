using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Agent.App.Tests;

internal sealed class TestHostEnvironment(string _contentRootPath) :
    IHostEnvironment
{
  public string EnvironmentName { get; set; } = "Test";

  public string ApplicationName { get; set; } =
      "PitCrew.Support.Agent.App.Tests";

  public string ContentRootPath { get; set; } = _contentRootPath;

  public IFileProvider ContentRootFileProvider { get; set; } =
      new NullFileProvider();
}
