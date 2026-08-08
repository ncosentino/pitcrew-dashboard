# UX terminology and status language

This glossary owns operator-facing terminology where similar words could otherwise
collapse distinct evidence or actions. Protocol and API contracts remain authoritative
for serialized names.

## Identity and topology

| Term | Meaning | Usage rule |
| --- | --- | --- |
| **Tenant** | The authorization and fleet boundary visible to one set of members. | Always name the active tenant in switching and administrative context. |
| **Host** | The physical machine, virtual machine, or Docker-visible operating environment whose resources are being observed. | Use for CPU, memory, operating-system, Docker runtime, and pressure evidence. |
| **Node** | The enrolled Dashboard identity authenticated by one connector, normally representing one host. | Lead with the node display name. Present the stable node ID as secondary, copyable metadata. |
| **Connector** | The outbound process that reads local PitCrew state and synchronizes credential-free evidence to Dashboard. | Connector health describes delivery behavior, not host or manager health. |
| **Profile** | One manager scope and configuration reported by a node. | Use the profile ID as its human-recognizable name until a separate display name exists. |
| **Target** | One repository or scale-set activation target inside an autoscaled profile. | Keep target-local evidence separate from profile totals. |

Do not use **host**, **node**, **connector**, and **profile manager** as interchangeable
names for the same failure domain.

Use **server** only when setup copy genuinely refers to the machine an operator is
enrolling. Existing generic "server" labels should migrate toward **node** or **host**
when the distinction is known.

## Work and capacity

| Term | Meaning | Usage rule |
| --- | --- | --- |
| **Slot** | A manager-owned local identity and capacity position for one potential worker. | Use when discussing desired state, slot lifecycle, or detailed manager evidence. |
| **Worker** | The local runner process or container occupying a slot. | Use for local process, resource, image, activity, and exit evidence. |
| **GitHub runner** | The registration visible to GitHub Actions. | Never infer registration or eligibility from a running local worker. |
| **GitHub job** | The correlated Actions job currently assigned to a worker. | Link to GitHub for inspection or cancellation; Dashboard does not cancel it. |
| **Configured maximum** | The administrative ceiling permitted for a profile. | Label explicitly; do not place it in an unlabeled slash-separated tuple. |
| **Target** | The manager's current activation target. | Distinguish from the configured maximum and from observed workers. |
| **Local slots** | Workers currently observed by the local manager. | Keep separate from GitHub eligibility. |
| **GitHub eligible** | Slots with current registration evidence that can accept GitHub work. | Render `Unknown` or `Unavailable` when the contract cannot provide it; never substitute the local count. |
| **Busy** | A worker with manager-owned activity or current-job evidence showing work. | Resource use alone is not proof of busy activity. |
| **Draining** | A worker allowed to finish current work while new admission is withheld. | Do not describe draining as stopped or cancelled. |

## Evidence state

| Term | Meaning | Usage rule |
| --- | --- | --- |
| **Current** | Evidence is within the source's accepted freshness boundary. | Include the observation or generation time when it affects a decision. |
| **Stale** | Evidence exists but is older than the accepted freshness boundary. | Keep the value visible and state that it may no longer describe current conditions. |
| **Last known** | The newest retained value from a source that is now offline or unavailable. | Prefix the value or group; never present it as current. |
| **Retained** | Durable historical or replayed evidence preserved after its live state changed. | Name the retained interval or observation time. |
| **Partial** | Some required sources reported and others did not. | Name the included and missing sources when useful. |
| **Unavailable** | The source did not provide a usable value. | Do not convert to zero, an empty healthy state, or a guessed cause. |
| **Unknown** | The contract supports the concept, but the value cannot be determined from available evidence. | Use sparingly and explain what evidence is missing. |
| **Measured zero** | The source explicitly measured a numeric value of zero. | Render `0`; do not label it unavailable. |
| **Inferred** | A conclusion derived from another signal rather than authoritative evidence. | Do not present inferred workload, registration, identity, or health as fact. |

## Incident lifecycle

| Term | Meaning | Usage rule |
| --- | --- | --- |
| **Triggered** | A debounced condition crossed its incident threshold and remains active. | Lead with severity, affected identity, and direct evidence. |
| **Acknowledged** | An operator has seen and taken ownership of an active incident. | Acknowledgement never means the condition is resolved. Use immediate acknowledgement with a short undo or unacknowledge path. |
| **Resolved** | Authoritative evidence shows the triggering condition ended. | Preserve the incident in bounded history with its resolved time. |
| **Warning** | Material degradation that needs attention but is not the highest urgency. | Pair the label with text; do not rely on amber alone. |
| **Critical** | The highest-severity active condition in the current incident model. | Pair the label with text and keep its evidence path visible. |

## Operations

| Term | Meaning | Usage rule |
| --- | --- | --- |
| **Pause new work** | Set profile admission capacity to zero while existing busy workers drain normally. | State explicitly that running GitHub jobs are not cancelled or preempted. |
| **Resume** | Restore the recorded pre-pause capacity through the typed capacity operation. | Name the restored maximum and the pause command it resumes. |
| **Manager recovery** | Restart one wedged profile manager through the locally constrained recovery operation. | Do not describe this as repairing Docker, the host, a worker, or GitHub. |
| **Rotate credential** | Replace an active credential with a new value and invalidate the old value according to its contract. | Confirm identity, scope, expiry, and one-time value handling before execution. |
| **Revoke** | Permanently invalidate the selected enrollment or diagnostic credential. | Use explicit confirmation and name what stops working. |
| **Prepare diagnostics** | Download credential-free Dashboard context for a separately authorized host diagnostic collection. | State that the exact affected GitHub run or job still needs to be supplied. |

## Copy rules

- Lead with the human-readable entity or task; place stable IDs in secondary
  monospaced metadata.
- Name the source and freshness of evidence before asking the operator to act.
- Use one term for one concept. Do not vary words for tone.
- Controls name their exact action and object.
- Errors name what failed and the available recovery path.
- Consequential confirmation names expected effects, prohibited effects, and the
  identity or fence being acted on.
- Never use **healthy**, **current**, **resolved**, or **zero** as a success-shaped
  fallback when evidence is missing.
