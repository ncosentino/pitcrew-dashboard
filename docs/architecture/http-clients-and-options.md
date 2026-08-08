# HTTP clients and options

Configuration types define startup/runtime policy; HTTP client types implement one
external transport boundary. Needlr source generation connects both without repeated
registration code.

## General options

`[Options]` infers a configuration section from the type name by removing the
`Options` suffix. Explicit section paths are preferable when configuration already
belongs to a feature hierarchy.

Startup validation turns missing or invalid configuration into a deployment/startup
failure instead of a delayed request failure. Data annotations keep simple constraints
near the configuration shape.

Choose the options interface by reload behavior:

- `IOptions<T>` for stable singleton configuration;
- `IOptionsSnapshot<T>` for request/scoped reload;
- `IOptionsMonitor<T>` for long-lived consumers that observe changes.

Named options represent multiple instances of one shape without creating duplicate
types.

## Named HttpClient generation

`[HttpClientOptions]` produces both options binding and named `HttpClient`
registration. Capability interfaces keep generated wiring proportional to the options
surface.

The client name is a compile-time identity. Attribute name, literal `ClientName`, and
type-name fallback cannot disagree, and two types cannot claim the same name.

An explicit feature-owned section keeps related configuration together. The
`HttpClients:<Name>` convention remains a fallback when no stronger owner exists.

## Client lifetime

`IHttpClientFactory` pools handlers, rotates them to account for DNS changes, and
centralizes named configuration. Constructing `HttpClient` directly or injecting an
unqualified client bypasses that lifecycle.

Factory-created clients are cheap wrappers and can be disposed after one operation
without destroying the pooled handler.

## Boundary ownership

An HTTP client:

- constructs and sends protocol requests;
- validates transport/status/content;
- maps external payloads into boundary/domain values;
- propagates cancellation.

Business decisions, retries with domain meaning, and cross-feature workflows belong
outside the transport client.

Tests replace the handler/factory boundary with deterministic responses. They assert
request construction, cancellation, response mapping, and failure behavior rather than
retesting Needlr's generated registration.
