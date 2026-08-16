using System.Buffers.Binary;
using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal static class SupportBrokerPipeCodec
{
  public static async Task WriteAsync<T>(
      Stream stream,
      T value,
      CancellationToken cancellationToken)
  {
    var payload = JsonSerializer.SerializeToUtf8Bytes(value);
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
    await stream.WriteAsync(length.ToArray(), cancellationToken);
    await stream.WriteAsync(payload, cancellationToken);
    await stream.FlushAsync(cancellationToken);
  }

  public static async Task<T?> ReadAsync<T>(
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
    return JsonSerializer.Deserialize<T>(payload);
  }
}
