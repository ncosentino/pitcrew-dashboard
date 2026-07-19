using NexusLabs.Needlr.AspNet;
using NexusLabs.Needlr.Injection;
using NexusLabs.Needlr.Injection.SourceGen;
using NexusLabs.Needlr.Serilog;

using CancellationTokenSource cts = new();
await new NeedlrSerilogBootstrapper()
    .RunAsync(async (context, ct) =>
    {
      var webApplication = new Syringe()
          .UsingSourceGen()
          .ForWebApplication()
          .UsingOptions(() => CreateWebApplicationOptions.Default
              .UsingCurrentProcessCliArgs()
              .UsingLogger(context.Logger))
          .BuildWebApplication();
      await webApplication.RunAsync(ct);
    }, cts.Token);

// Exposed so consumers can opt into integration tests with
// Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>.
// See PitCrew.Dashboard.WebApi.Tests.HostingTests for full-host examples.
public partial class Program;
