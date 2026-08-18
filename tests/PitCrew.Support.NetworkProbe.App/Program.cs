using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var resultPath = builder.Configuration["result"] ??
    throw new InvalidOperationException("A result path is required.");
var address = builder.Configuration["address"] ??
    throw new InvalidOperationException("A network address is required.");
var portText = builder.Configuration["port"] ??
    throw new InvalidOperationException("A network port is required.");
if (!System.Net.IPAddress.TryParse(address, out var parsedAddress))
{
  throw new InvalidOperationException("The network address must be an IP address.");
}
if (!int.TryParse(
        portText,
        System.Globalization.CultureInfo.InvariantCulture,
        out var port) ||
    port is < 1 or > 65535)
{
  throw new InvalidOperationException("The network port is invalid.");
}
builder.Services.AddSingleton(
    new NetworkProbeOptions(resultPath, parsedAddress, port));
builder.Services.AddHostedService<NetworkProbeWorker>();
builder.Services.AddWindowsService(service =>
{
  service.ServiceName = "PitCrewSupportBroker";
});
await builder.Build().RunAsync();

internal sealed record NetworkProbeOptions(
    string ResultPath,
    System.Net.IPAddress Address,
    int Port);

internal sealed class NetworkProbeWorker(
    NetworkProbeOptions _options,
    IHostApplicationLifetime _lifetime) : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    var result = "denied";
    using var client = new System.Net.Sockets.TcpClient();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        stoppingToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(8));
    try
    {
      await client.ConnectAsync(
          _options.Address,
          _options.Port,
          timeout.Token);
      result = client.Connected ? "connected" : "denied";
    }
    catch (System.Net.Sockets.SocketException)
    {
    }
    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
    {
    }
    await File.WriteAllTextAsync(
        _options.ResultPath,
        result,
        stoppingToken);
    _lifetime.StopApplication();
  }
}
