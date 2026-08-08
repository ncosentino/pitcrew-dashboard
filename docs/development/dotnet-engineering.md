# .NET engineering conventions

The generated .NET guidance favors analyzable, deterministic code over local
convenience. Exact rules stay in path-scoped instructions; this page explains the
design tradeoffs behind the broad conventions.

## Generated logging

`LoggerMessage` source generation moves template parsing and delegate construction to
compile time. Disabled log levels avoid message allocation and value-type boxing.

Keep a few generated methods on the consuming partial class. Once a feature has a
larger event vocabulary, a dedicated `{Feature}Log` class keeps names and levels
coherent without creating one logger class per consumer.

Scopes carry stable request, message, job, or business identifiers through downstream
calls. One scope at the entry boundary is enough; repeated nested scopes create
duplicate properties and unclear ownership.

Log severity communicates operational meaning:

- debug is diagnostic detail;
- information is an expected business event;
- warning is an expected or transient failure;
- error means a dependency/configuration/system is behaving incorrectly;
- critical means the process cannot continue safely.

Sensitive values and unbounded user-controlled fields do not belong in logs. Metadata
should remain useful without exposing bodies, credentials, tokens, or high-cardinality
payloads.

## Controllable time

System clocks make tests depend on wall time and make delay/timeout behavior slow or
flaky. `TimeProvider` is the BCL seam for current time, elapsed time, timers, delays,
periodic work, timeout cancellation, and `WaitAsync`.

Production code injects `TimeProvider`; infrastructure registers
`TimeProvider.System`; tests replace it with `FakeTimeProvider`. Advancing a fake clock
before the system under test has registered its timer can race with the test, so
fixtures coordinate registration before advancing.

`DateTimeOffset` preserves an offset and round-trips unambiguously. Convert to
`DateTime` only at a schema or API boundary that requires it.

## File ownership

One public/internal type per file keeps symbol discovery, ownership, and diffs
predictable. Private nested response/row DTOs are useful when they exist solely to
serve one HTTP client or repository and never escape it.

The deciding question is whether another type can observe the nested type through a
member signature or reuse it elsewhere. Once it escapes, it needs its own named file.

## Build and test entrypoints

The repository pins Microsoft.Testing.Platform. TUnit uses `dotnet test`; xUnit
projects under the MTP-pinned repository run as executables. The distinction belongs
to the test framework/runtime contract, not contributor preference.

Zero-test projects fail because an empty green test project is success-shaped. A new
test project includes a real passing test from its first commit.

Local work runs the narrow project or gate that proves the change. Complete suites
belong to configured CI/PitCrew capacity.

## Generated factories

Needlr can resolve services, but it cannot invent per-call strings, ids, delegates, or
other runtime values. Generated factories keep service dependencies container-owned
while exposing only runtime values on `Create(...)`.

The decorated type is not itself a service. Consumers depend on the generated factory,
which prevents bypassing the intended construction boundary.
