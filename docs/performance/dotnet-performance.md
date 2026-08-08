# .NET performance

Performance guidance applies when a path is repeated, allocation-sensitive,
throughput-sensitive, or latency-sensitive. Do not add complexity merely because an
API is theoretically faster.

## Measure first

Prefer clear framework primitives until a benchmark or production measurement
identifies a meaningful bottleneck. Optimizations that change ownership, lifetime,
threading, or CPU-specific behavior require stronger evidence than a micro-optimization
with no semantic cost.

## Round trips and batching

Database, HTTP, message, and remote-cache calls cross process or network boundaries.
Fetching a collection and then issuing one I/O call per item creates an N+1 pattern.

Bulk APIs should:

1. deduplicate identifiers;
2. make one round trip;
3. index results once;
4. handle missing keys explicitly;
5. return only fields the caller needs.

`Task.WhenAll` can overlap N calls, but it remains N calls. It is not a substitute for
a real batch contract.

## Buffers and pools

Pooling helps only when allocation is frequent or large enough to matter. Every pool
introduces lifetime and reset obligations:

- a rented owner is disposed exactly once;
- mutable contents are not shared without synchronization;
- sensitive contents are cleared before return;
- an object-pool policy resets all state;
- retained pool sizes are bounded.

Use span-backed owners only within synchronous scope. Memory-backed owners are needed
across `await`. Raw `ArrayPool<T>` is the fallback when the project cannot use the
copy-safe NexusLabs helpers.

## Span and unsafe APIs

`Span<T>` is stack-bound and synchronous. `Memory<T>` carries data into fields or async
operations.

`CollectionsMarshal`, `MemoryMarshal`, `stackalloc`, pinning, and skipped-local
initialization remove safety checks. Each use needs an explicit lifetime/size/alignment
argument and a bounded fallback where applicable.

## Lookup and formatting structures

`SearchValues<T>`, frozen collections, and parsed composite formats have expensive
construction and cheap reuse. Build them once for trusted, stable data.

When a boundary is already UTF-8, span-based parsing/formatting avoids an intermediate
UTF-16 string. Do not introduce UTF-8 APIs where the surrounding system immediately
converts back to strings.

## Streaming and serialization

Pipelines require separate consumed and examined positions. Advancing incorrectly can
spin or retain data indefinitely.

Generated System.Text.Json contexts support trimmed/Native AOT builds. Reader/writer
APIs are appropriate for large or streaming payloads where materializing the full
document is the measured problem.

`IBufferWriter<T>` advances exactly what was written. `ValueTask` is consumed once;
convert it to `Task` before caching or sharing.

## Concurrency and hardware acceleration

Use `System.Threading.Lock` for new synchronous locking and never await while holding
it. Bounded channels state their drop/backpressure policy explicitly.

SIMD paths keep a scalar fallback. Inlining hints, hand vectorization, cache-line
padding, pooling async builders, and no-GC regions require benchmark evidence on the
target workload and architecture.
