---
# AUTO-GENERATED from .github/instructions/genesis/performance/concurrency-and-vectorization.instructions.md — do not edit
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
# Concurrency & Vectorization Performance

## Locking

- Prefer `System.Threading.Lock` over `lock` on a plain `object` for new code.
- MUST keep the field's declared type as `Lock`, not `object` — assigning or upcasting it to
  `object` silently reverts to `Monitor`-based locking (the compiler only warns, CS9216).
- MUST NOT `await` inside a `lock`/`EnterScope()` block on a `Lock` — the scope is a `ref struct`
  and cannot cross an `await` (compile error).

## False sharing

- Pad hot fields that are concurrently written by different threads (per-thread counters,
  contended state) into their own cache-line-sized struct (explicit `[StructLayout]` padding, 64
  bytes on x64 / 128 on ARM64) to avoid cross-core cache-line invalidation storms.

## Vectorization

- Guard `Vector256<T>`/`Vector512<T>` usage behind `IsHardwareAccelerated` and keep a scalar
  fallback path — there is no automatic fallback on hardware lacking the required instruction set.
- Prefer `TensorPrimitives` for span-based numeric operations (dot product, sum, sigmoid, etc.)
  over a hand-written loop before hand-vectorizing yourself.

## Method inlining hints

- MUST NOT apply `[MethodImpl(MethodImplOptions.AggressiveInlining)]` beyond a tiny (a few IL
  instructions), extremely hot method — the JIT can refuse it anyway, and applying it broadly
  bloats code size.
- MUST NOT apply `AggressiveOptimization` as a default habit — it skips tiered/PGO profiling
  entirely, which can produce **worse** steady-state code for anything beyond a trivial method.
  Reserve both attributes for cases backed by a benchmark showing a real win.

## Channels & periodic work

- Choose `BoundedChannelFullMode` deliberately: `Wait` for genuine backpressure, `DropOldest`/
  `DropNewest`/`DropWrite` only when losing items is acceptable. MUST NOT leave a bounded channel's
  mode as an accidental default without considering which behavior the scenario needs.
- Use `PeriodicTimer` instead of a `Task.Delay` loop or `System.Threading.Timer` for periodic
  background work — it coalesces missed ticks and avoids period drift from variable processing
  time.

## Extreme latency-critical sections

- `GC.TryStartNoGCRegion` guarantees no GC during a precisely budgeted section — MUST size the
  budget accurately (including LOH allocations via the separate `lohSize` parameter). Exceeding the
  budget does not throw; it silently ends the no-GC region and collects anyway, defeating the
  guarantee exactly when it was needed most.
