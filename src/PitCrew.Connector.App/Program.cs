using Microsoft.Extensions.Hosting;

using NexusLabs.Needlr.Hosting;
using NexusLabs.Needlr.Injection;
using NexusLabs.Needlr.Injection.SourceGen;
using NexusLabs.Needlr.Serilog;

using CancellationTokenSource cts = new();
await new NeedlrSerilogBootstrapper()
    .RunAsync(async (context, ct) =>
    {
      var host = new Syringe()
          .UsingSourceGen()
          .ForHost()
          .UsingOptions(() => CreateHostOptions.Default
              .UsingCurrentProcessArgs()
              .UsingApplicationName("PitCrew.Connector.App")
              .UsingLogger(context.Logger))
          .BuildHost();
      await host.RunAsync(ct);
    }, cts.Token);
