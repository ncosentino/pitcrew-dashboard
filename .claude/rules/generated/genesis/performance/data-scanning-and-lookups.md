---
# AUTO-GENERATED from .github/instructions/genesis/performance/data-scanning-and-lookups.instructions.md — do not edit
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
# Data Scanning & Lookup Performance

## SearchValues<T>

- MUST construct `SearchValues<T>` exactly once (a `static readonly` field) and reuse it —
  construction is an intentionally expensive analysis step; building it per-call or per-request
  defeats the entire point.
- Use `span.IndexOfAny(mySearchValues)`/`ContainsAny(...)` instead of a manual per-character/byte
  loop or a chain of `IndexOf`/`Contains` calls when testing membership against a fixed set of
  values.

## FrozenDictionary<TKey,TValue> / FrozenSet<T>

- Use for a lookup table built once (e.g. at startup) and read many times afterward — construction
  is far slower than `Dictionary`, but lookups are faster.
- MUST NOT rebuild a `FrozenDictionary`/`FrozenSet` on a frequently-repeated path — that pays the
  expensive construction cost every time instead of once.
- MUST NOT construct one from untrusted/adversarial input — construction cost scales with key
  content, which is a denial-of-service vector.

## Span-keyed lookups

- Use `dictionary.GetAlternateLookup<ReadOnlySpan<char>>()` (or `TryGetAlternateLookup` to avoid an
  exception when the comparer doesn't support it) to look up by span instead of calling
  `.ToString()` on a slice just to use it as a dictionary key.

## Repeated formatting

- Cache a `CompositeFormat` (`CompositeFormat.Parse(...)`, once) instead of passing the same
  literal format string to `string.Format`/`AppendFormat` repeatedly on a hot path — this avoids
  re-parsing the format string on every call.

## UTF-8 formatting

- Prefer a type's `IUtf8SpanFormattable`/`IUtf8SpanParsable<T>` implementation when producing or
  consuming UTF-8 directly (JSON, HTTP) — it skips the intermediate UTF-16 `string` allocation
  entirely.
- MUST NOT reference `Utf8String`/`Utf8Span` — proposed but withdrawn; they do not exist in any
  shipping .NET release.
