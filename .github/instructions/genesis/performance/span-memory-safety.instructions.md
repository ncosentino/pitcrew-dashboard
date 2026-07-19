---
applyTo: "**/*Service.cs,**/*Repository.cs,**/*Worker.cs,**/*Job.cs,**/*Consumer.cs,**/*CarterModule.cs,**/*Client.cs,**/*Handler.cs,**/*UnitOfWork.cs,**/*Parser.cs,**/*Serializer.cs,**/*Reader.cs,**/*Writer.cs,**/*Stream.cs,**/*Buffer.cs"
---

# Span, Memory & Unsafe Escape Hatches

## Span<T> vs Memory<T>

- Use `Span<T>`/`ReadOnlySpan<T>` only for synchronous, stack-bound work. MUST use `Memory<T>`/
  `ReadOnlyMemory<T>` for anything stored in a field or crossing an `await`.

## CollectionsMarshal / MemoryMarshal

- MUST NOT mutate a `List<T>` (or resize/rehash a `Dictionary`) while holding a span or ref
  obtained from `CollectionsMarshal.AsSpan`/`GetValueRefOrAddDefault`/`GetValueRefOrNullRef` — the
  underlying array can be swapped out from under it with no exception, only silent corruption.
- `MemoryMarshal.Cast`/`CreateSpan` skip bounds and alignment checking entirely — treat them as
  unsafe. Verify sizes and alignment yourself; a wrong length is silent memory corruption, not an
  exception.

## stackalloc

- MUST guard any non-trivial or caller-influenced `stackalloc` size with a ceiling and a
  pooled/heap fallback above it. An oversized or unbounded `stackalloc` overflows the stack — an
  unrecoverable crash, not a catchable exception.
- MUST NOT apply `[SkipLocalsInit]` unless every `stackalloc`'d or local byte is fully written
  before it's read. Applied broadly "for perf," it silently exposes leftover stack garbage instead
  of a deterministic value.

## Pinning

- Use `GCHandle.Alloc(_, GCHandleType.Pinned)`/`fixed` only for native/P-Invoke interop — not as a
  substitute for `Span<T>`/`Memory<T>` in managed-only code.
- MUST free every pinned handle via `try`/`finally` — an unfreed pin permanently blocks GC
  compaction of that memory for the rest of the process's life.
