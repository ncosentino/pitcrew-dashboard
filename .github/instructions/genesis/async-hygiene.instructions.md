---
applyTo: "**/*.cs"
---

# Async hygiene

- Keep asynchronous call chains asynchronous. Use `await` rather than blocking on a `Task` with
  `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()`.
- Use `Channel.CreateBounded<T>` with an explicit capacity when a channel provides queueing or
  backpressure. If a synchronous producer must feed an asynchronous consumer, use a synchronous
  backpressure boundary rather than blocking on an asynchronous operation.
