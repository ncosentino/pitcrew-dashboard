# Connector health journal

Every connector keeps bounded, structured health evidence beside its protected
identity directory. This local journal explains why synchronization stopped even
when the Dashboard cannot receive a heartbeat.

The connector writes:

- `health/connector-health.json` — atomic current and most recently recovered
  outage state;
- `health/connector-events.jsonl` — rolling newest 256 lifecycle, failure, and
  recovery events;
- `health/connector-health-acknowledgement.json` — bounded event identifiers
  durably accepted by Dashboard.

For the Windows service installer these files are below
`C:\ProgramData\PitCrew\Connector\health`. For systemd installations they are
below `/var/lib/pitcrew-connector/health`. Container deployments use the
existing persistent connector-data volume.

## Recorded evidence

The journal records:

- connector process start and stopping intent;
- synchronization attempts and successes;
- incomplete PitCrew observed-state reads;
- network, timeout, rate-limit, server, and local-I/O failures;
- enrollment, payload, and credential rejection;
- the active outage identifier and start time;
- consecutive failures and the scheduled retry time;
- the most recently recovered outage.

Failure details are selected from bounded connector-owned messages. Exception
text, URLs, query strings, credentials, enrollment codes, payloads, environment
values, absolute PitCrew paths, connector identity, and stack traces are not
written.

Profile identifiers are retained only when they satisfy the public PitCrew
profile-ID contract.

## Schema

Both files use camel-case JSON with `schemaVersion: 1`. Timestamps are UTC
RFC 3339 values. Nullable fields are present with `null` when no value has been
observed.

`connector-health.json` contains:

| Field | Meaning |
| --- | --- |
| `state` | `starting`, `healthy`, `degraded`, or `stopping` |
| `processStartedAt`, `updatedAt` | Current process start and last journal update |
| `lastAttemptAt`, `lastSuccessAt` | Most recent synchronization attempt and accepted synchronization |
| `activeOutageId`, `activeOutageStartedAt` | Durable active outage identity and interval start |
| `lastFailureAt`, `lastFailureCategory`, `lastFailureProfileId`, `lastFailureDetail` | Most recent sanitized failure evidence |
| `lastFailureHttpStatusCode`, `lastFailureRejectionReason` | Optional numeric HTTP status and allowlisted synchronization rejection reason |
| `consecutiveFailures`, `nextRetryAt` | Retry state derived from the connector's actual scheduled delay |
| `lastRecoveredOutageId`, `lastRecoveredOutageStartedAt`, `lastRecoveredAt`, `lastRecoveredFailureCategory` | Most recently recovered outage interval |

Each `connector-events.jsonl` line contains:

| Field | Meaning |
| --- | --- |
| `eventId`, `kind`, `occurredAt`, `state` | Event identity, type, time, and resulting connector state |
| `outageId`, `outageStartedAt` | Related active or recovered outage |
| `failureCategory`, `profileId`, `detail` | Sanitized bounded failure evidence |
| `httpStatusCode`, `rejectionReason` | Optional HTTP rejection metadata; never response bodies or arbitrary server text |
| `consecutiveFailures`, `retryDelaySeconds` | Retry evidence at the time of the event |

Event kinds are `process-started`, `process-stopping`,
`synchronization-succeeded`, `synchronization-failed`,
`observation-incomplete`, `enrollment-failed`, `rejected`, and `recovered`.

Failure categories distinguish state-root, profile-directory, profile-state,
network, timeout, rate-limit, server, local-I/O, payload, credential,
enrollment, and configuration failures. Category values are stable
machine-readable identifiers such as `profile-state-invalid`,
`synchronization-network`, and `credential-rejected`.

## Observation isolation and recovery

Profile discovery uses `static-profile.json`, `desired-capacity.json`, or
`observed-state.json` as durable profile markers. Auxiliary state directories
without those markers are not profiles. A configured profile with missing or
invalid observed state still makes inventory partial or unavailable, including
after connector restart; a missing file is not evidence that the profile was
removed.

Connector preflight and Dashboard ingestion share the same semantic observation
validator. Only validated observations enter the last-good cache. Invalid
profiles retain validated cached evidence when available while healthy profiles
continue synchronizing. Incomplete inventory does not advertise remote operation
capabilities.

HTTP rejection is a synchronization outcome, not a reason to terminate the
worker. Rejections are journaled with a scheduled, jittered retry so corrected
state or a repaired endpoint can recover without a service restart. HTTP 408 is
a timeout, 429 is rate limiting, and 5xx responses remain retryable server
failures. Revoked credentials remain degraded; explicit re-enrollment still
requires a locally configured one-time enrollment code.

The generic-host composition invokes the feature's generated transport registrar,
so native and container connectors resolve the same pooled `PitCrewConnector`
client. Its timeout defaults to 30 seconds and can be overridden through
`PitCrew:Connector:HttpClient:Timeout`. The same registrar owns connector options
binding and validation; binding again would append duplicate operator allowlist
and recipe entries, causing installed-service startup validation to fail.

New Dashboards return the bounded `X-PitCrew-Sync-Rejection` header, identifying
the rejected contract family without including profile values. Older Dashboards
remain compatible: the connector records the HTTP status with no specific
reason. Unknown, numeric, or arbitrary header values are not retained. These
optional diagnostic fields are local-only; health replay stays compatible with
the existing protocol and does not forward response bodies.

Unexpected worker failures terminate an installed Windows service with a
nonzero process exit before a normal service-stop notification can bypass SCM
failure recovery. Console and container hosts also receive a nonzero exit
status. Intentional operator shutdown remains a clean stop. Payload and
credential rejections do not create an SCM restart loop.

The `Connector regression` workflow runs focused connector and Fleet tests on
the PitCrew general-purpose pool, either by manual dispatch or the explicit
`ci:connector-regression` label on a same-repository PR. It does not run
untrusted fork code and does not add another default PR test matrix. It does
not replace the full armed-PR or installed-service release qualification.

## Retention and durability

All three files use schema version 1 and are replaced atomically. The event journal
retains only the newest 256 valid entries. Existing input is capped at
1,048,832 bytes and individual lines are capped at 4,096 characters before
deserialization. On Linux, the health directory is owner-only and files are
owner read/write.

An active outage survives connector restart. A later successful synchronization
records a recovery event and retains the recovered outage interval in the
current snapshot.

Journal write failures are logged but do not stop connector synchronization.
Invalid existing journal content is ignored and replaced with a new local
projection. A write can delay connector work for at most one second. Deferred
state transitions retain FIFO order in a bounded 256-entry memory buffer;
attempt-only updates are dropped while a write is already active. If that
buffer saturates, the oldest deferred update is discarded and a warning is
written to the existing connector log.

Readers must check `schemaVersion`. Version 1 readers ignore unknown JSON
properties, invalid JSONL entries, overlong lines, and entries with another
schema version. An invalid or unsupported current-health document starts a new
version 1 projection; journal files are diagnostic state, not configuration.

## Retrospective Dashboard replay

Connector protocol 10 carries the current snapshot plus unacknowledged journal
events inside the existing outbound synchronization request. There is no second
health endpoint or inbound node channel.

Dashboard validates the fixed state, event-kind, failure-category, profile-ID,
timestamp, retry, and connector-owned detail contracts before committing the
replay with the node heartbeat. SQLite stores one current health projection per
node and a bounded idempotent event ledger.

The synchronization response acknowledges every event ID committed in that
transaction. The connector merges those IDs into
`connector-health-acknowledgement.json`, intersects them with the events still
retained locally, and suppresses them from later requests. A lost response or
failed acknowledgement write causes harmless redelivery because Dashboard keys
events by node and event ID.

Unacknowledged failure, incomplete-observation, enrollment, and rejection
events make the next eligible connector poll synchronize even when profile
state is unchanged. Ordinary success and lifecycle events wait for the next
normal heartbeat or state change so replay does not amplify steady-state
traffic.

When an accepted request carried an active outage, the connector records the
successful synchronization locally and schedules an immediate follow-up. That
second request replays the exact recovery event instead of leaving Dashboard on
the pre-response degraded snapshot until the next normal heartbeat.

Protocol 1 through 9 connectors remain compatible and report no retrospective
health evidence. Protocol 10 also permits a missing replay when the local
journal is absent or unreadable; Dashboard must describe that reason as
unavailable rather than infer a cause.

## Current scope

The local journal remains the source of replayed evidence. Dashboard does not
receive connector text logs, absolute host paths, credentials, connector
identity, payloads, query strings, environment values, or stack traces.

## Dashboard reporting-loss presentation

Fleet responses include the connector-health snapshot committed with the
latest accepted synchronization. If that synchronization omitted replay
evidence, the current Fleet projection reports it unavailable instead of
reusing an older snapshot; the bounded event ledger remains retained in storage
and is not added to every browser poll.

When a connector is not reporting, Dashboard labels profile, capacity, worker,
resource, and hardware values as last-known evidence with their source
timestamps. Hardware whose latest collection succeeded is labelled
`latest reported`, not `current`, because connector liveness does not prove
physical-host liveness. Dashboard does not label the host offline from this
evidence.

When replayed evidence exists, node and incident views show the retained failure
category, affected profile when known, outage interval, retry evidence, and
recovery. When no replay has ever arrived, Dashboard states that the reason is
unavailable because Dashboard is not receiving connector reports;
acknowledgement remains a separate incident lifecycle action and never implies
diagnosis or resolution.

The read-only `Prepare diagnostics` action downloads schema version 1 preflight
JSON compatible with PitCrew's remote-diagnostics importer. It contains the
node ID, serialized `online`/`offline`/`revoked` connector state, last accepted
heartbeat, selected diagnostic mode, and retained incident category. The UI
presents `offline` as connector reporting loss rather than a host-liveness claim.
GitHub run metadata, independent endpoint probing, and release lookup remain
explicitly unavailable until the operator or agent supplies them. The document
contains no credential, command, local path, connector identity, or free-form
host input.
