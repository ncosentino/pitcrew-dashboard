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
| `consecutiveFailures`, `nextRetryAt` | Retry state derived from the connector's actual scheduled delay |
| `lastRecoveredOutageId`, `lastRecoveredOutageStartedAt`, `lastRecoveredAt`, `lastRecoveredFailureCategory` | Most recently recovered outage interval |

Each `connector-events.jsonl` line contains:

| Field | Meaning |
| --- | --- |
| `eventId`, `kind`, `occurredAt`, `state` | Event identity, type, time, and resulting connector state |
| `outageId`, `outageStartedAt` | Related active or recovered outage |
| `failureCategory`, `profileId`, `detail` | Sanitized bounded failure evidence |
| `consecutiveFailures`, `retryDelaySeconds` | Retry evidence at the time of the event |

Event kinds are `process-started`, `process-stopping`,
`synchronization-succeeded`, `synchronization-failed`,
`observation-incomplete`, `enrollment-failed`, `rejected`, and `recovered`.

Failure categories distinguish state-root, profile-directory, profile-state,
network, timeout, rate-limit, server, local-I/O, payload, credential,
enrollment, and configuration failures. Category values are stable
machine-readable identifiers such as `profile-state-invalid`,
`synchronization-network`, and `credential-rejected`.

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
