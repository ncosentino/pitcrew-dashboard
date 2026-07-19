---
# AUTO-GENERATED from .github/instructions/genesis/performance/streams-and-serialization.instructions.md — do not edit
paths:
  - "**/*Service.cs"
  - "**/*Repository.cs"
  - "**/*Worker.cs"
  - "**/*Job.cs"
  - "**/*Consumer.cs"
  - "**/*CarterModule.cs"
  - "**/*Client.cs"
  - "**/*Handler.cs"
  - "**/*UnitOfWork.cs"
  - "**/*Parser.cs"
  - "**/*Serializer.cs"
  - "**/*Reader.cs"
  - "**/*Writer.cs"
  - "**/*Stream.cs"
  - "**/*Buffer.cs"
---
# Streaming I/O & Serialization Performance

## System.IO.Pipelines

- MUST track `consumed` and `examined` as distinct positions in
  `PipeReader.AdvanceTo(consumed, examined)`: advance `consumed` past bytes fully processed, and
  advance `examined` to the buffer's end when more data is needed before the next message can be
  parsed. Advancing neither causes an immediate-return, 100%-CPU infinite loop.
- MUST NOT read `ReadResult.Buffer` after calling `AdvanceTo` — those segments may already be back
  in the pool.

## Parsing/formatting primitives

- Use `Utf8Parser`/`Utf8Formatter` to parse/format primitive values directly against
  `ReadOnlySpan<byte>`/`Span<byte>` instead of transcoding through an intermediate `string`.
- Use `Base64Url` for URL-safe base64 (JWTs, PKCE, tokens) instead of hand-rolled `+`/`/`/`=`
  substitution.

## JSON

- Use System.Text.Json source generation (`JsonSerializerContext` + `[JsonSerializable]`) for
  (de)serialization rather than the reflection-based default — required for NativeAOT/trimmed
  builds, and the fast path measurably speeds up serialization (deserialization does not get the
  same fast path).
- Use `Utf8JsonReader`/`Utf8JsonWriter` directly — not a full `JsonDocument`/POCO round-trip — for
  streaming or very large JSON.
- MUST pass `Utf8JsonReader` by `ref` into any helper method — it's a `ref struct`; passing by
  value copies it and the caller's read position never advances.

## IBufferWriter<T>

- Treat the `sizeHint` passed to `GetSpan`/`GetMemory` as a minimum, not an exact size — use the
  returned span/memory's actual length, not the hint, to know how much you can write.
- MUST call `Advance(count)` with the exact number of elements written for every `GetSpan`/
  `GetMemory` call — skipping it or double-calling it corrupts the writer's internal state.

## ValueTask

- MUST consume a `ValueTask`/`ValueTask<T>` exactly once (`await`, `.Result`, or `.AsTask()`) —
  never store one and await it twice, and never share one across threads. Call `.AsTask()` first
  if a cacheable, re-awaitable result is needed.
- Only add `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]` to a hot
  `async ValueTask<T>` method after benchmarking shows its state-machine allocation is a measured
  bottleneck — it is not a default-apply optimization.
