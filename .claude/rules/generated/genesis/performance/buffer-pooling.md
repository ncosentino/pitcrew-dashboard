---
# AUTO-GENERATED from .github/instructions/genesis/performance/buffer-pooling.instructions.md — do not edit
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
# Buffer & Object Pooling

Rent reusable buffers instead of allocating fresh ones in any code path that runs repeatedly
(per-request, per-item, per-message).

## ArrayPool<T>

- Use `pool.RentMemory(n)`/`pool.RentSpan(n)` (`NexusLabs.Framework.Buffers`) instead of manual
  `Rent`/`try`/`finally`/`Return`. Both return a disposable handle — bind to a single `using` and
  the array returns to the pool on every exit path.
- Use `RentSpan` for synchronous code — zero-allocation, and the compiler blocks it from ever
  escaping to the heap. Use `RentMemory` when the buffer must cross an `await` or be stored — one
  small heap allocation for the owner, but copies share that owner, so the array returns exactly
  once no matter how many copies exist.
- MUST NOT copy or pass a `RentSpan` handle by value — it creates a second owner of the same array,
  and disposing both returns it twice. The compiler does not catch this; the NLF0024 analyzer does.
- `RentMemory(n)`/`RentSpan(n)` return an array **at least** `n` long, never exactly `n` — read
  `Capacity` for the granted size, never assume it equals what was requested.
- MUST NOT hand a rented buffer's contents to another thread without external synchronization —
  the handle's ownership/disposal is safe to copy or share, but the underlying array is still
  shared mutable memory.
- Pass `clearOnReturn: true` for sensitive data — but know a full pool silently **drops** the array
  without clearing it. Zero sensitive contents yourself first if they must never survive.
- Prefer a dedicated pool (`ArrayPool<T>.Create(...)`) over `Shared` for very large buffers,
  secret-bearing buffers, or buffers whose retention policy must stay isolated from unrelated
  callers.
- Only fall back to raw `ArrayPool<T>.Rent`/`Return` when the `NexusLabs.Framework` dependency is
  unavailable — MUST still return via `try`/`finally` there, and MUST NOT return an array that
  wasn't obtained from that same pool (`Return` throws on a size mismatch).

## MemoryPool<T> / IMemoryOwner<T>

- Prefer `pool.RentMemory(n)` (see above) over `MemoryPool<T>.Shared.Rent(n)` — the same
  `IMemoryOwner<T>` shape, but with a copy-safe disposal guarantee. Reach for `MemoryPool<T>.Shared`
  only in code that cannot take the `NexusLabs.Framework` dependency.
- MUST scope any `IMemoryOwner<T>` with `using`. The `Memory<T>` is valid only while the owner is
  alive — never store `.Memory` past `Dispose()`.

## ObjectPool<T> (Microsoft.Extensions.ObjectPool)

- Use for pooling non-array reusable objects (`StringBuilder`, parser/serializer scratch state),
  not just arrays.
- MUST NOT assume an object handed back by `Get()` is fully reset — that is the pool policy's
  `Return` responsibility. Verify (or write) the policy so it fully clears state before an object
  re-enters the pool.
- Treat a leased object as single-threaded for the duration of the lease; MUST NOT use it after
  calling `Return`.

## RecyclableMemoryStream

- Use `Microsoft.IO.RecyclableMemoryStream` instead of `new MemoryStream()` for streams that are
  large (>85 KB) or frequently created — plain `MemoryStream` backs onto the Large Object Heap past
  that size and fragments it over time.
- MUST NOT call `.ToArray()` on a recyclable stream — it always allocates a fresh copy and defeats
  the pooling. Use `GetReadOnlySequence()` to read instead.
- MUST bound `MaximumFreeSmallPoolBytes`/`MaximumFreeLargePoolBytes` — an unbounded pool is an
  unbounded memory leak under a load spike.
