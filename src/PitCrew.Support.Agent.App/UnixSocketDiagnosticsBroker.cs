using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace PitCrew.Support.Agent.App;

internal sealed class UnixSocketDiagnosticsBroker(string _socketPath) : ILocalDiagnosticsBroker
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  public async Task<LocalDiagnosticsResult> ExecuteAsync(
      LocalDiagnosticsRequest request,
      CancellationToken cancellationToken)
  {
    using var socket = new Socket(
        AddressFamily.Unix,
        SocketType.Stream,
        ProtocolType.Unspecified);
    await socket.ConnectAsync(
        new UnixDomainSocketEndPoint(_socketPath),
        cancellationToken);
    await using var stream = new NetworkStream(socket, ownsSocket: false);
    await WriteAsync(stream, request, cancellationToken);
    var response = await ReadAsync<BrokerResponseEnvelope>(
        stream,
        cancellationToken) ??
        throw new IOException("Broker returned an empty response.");
    if (!string.Equals(response.Status, "Succeeded", StringComparison.Ordinal) ||
        response.Response is null)
    {
      throw LocalDiagnosticsBrokerRejectedException.FromStatus(
          response.Status);
    }
    return new LocalDiagnosticsResult(
        response.Response.Report,
        response.Response.Markdown);
  }

  private static async Task WriteAsync<T>(
      Stream stream,
      T value,
      CancellationToken cancellationToken)
  {
    var payload = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
    await stream.WriteAsync(length.ToArray(), cancellationToken);
    await stream.WriteAsync(payload, cancellationToken);
    await stream.FlushAsync(cancellationToken);
  }

  private static async Task<T?> ReadAsync<T>(
      Stream stream,
      CancellationToken cancellationToken)
  {
    var lengthBuffer = new byte[4];
    await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
    if (length is <= 0 or > 1_048_576)
    {
      return default;
    }
    var payload = new byte[length];
    await stream.ReadExactlyAsync(payload, cancellationToken);
    return JsonSerializer.Deserialize<T>(payload, _jsonOptions);
  }

  private sealed record BrokerResponseEnvelope(
      string Status,
      BrokerResponse? Response,
      string? Error);

  private sealed record BrokerResponse(
      JsonElement Report,
      string Markdown);
}
