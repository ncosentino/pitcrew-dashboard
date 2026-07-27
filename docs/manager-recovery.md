# Manager recovery

Protocol v4 adds one additional opt-in write capability: restarting the manager
of an existing PitCrew profile that is still running but wedged.

Recovery is separate permission from capacity. A host that already executes
capacity commands never gains recovery permission by upgrading; an operator must
opt in explicitly.

The dashboard never supplies a path, executable, or argument. The connector
reconstructs every argument from local configuration and invokes PitCrew's
public recovery primitive:

```text
Setup-Runner.ps1 -Profile <profile> -RecoverManager `
    -ExpectedManagerInstanceId <instance> `
    -ExpectedGeneration <generation> `
    -ExpectedDesiredStateHash <hash> `
    -RecoveryTimeoutSeconds <seconds>
```

## Deployment boundary

Recovery requires the connector to run as the supported host service. The
container connector remains read-only, never advertises the capability, and
rejects recovery commands.

## Configuration

Recovery is disabled by default. The installer writes host configuration
equivalent to:

```json
{
  "PitCrew": {
    "Connector": {
      "ManagerRecoveryEnabled": true,
      "AllowedManagerRecoveryProfiles": ["copilot-cli"],
      "RecoveryCommandTimeoutSeconds": 120,
      "RecoveryCommandMaximumExpirySeconds": 900,
      "RecoveryObservedStateMaximumAgeSeconds": 300,
      "RecoveryLedgerPath": "C:\\ProgramData\\PitCrew\\Connector\\recovery-ledger"
    }
  }
}
```

Environment-variable equivalents use .NET's double-underscore convention:

```text
PitCrew__Connector__ManagerRecoveryEnabled=true
PitCrew__Connector__AllowedManagerRecoveryProfiles__0=copilot-cli
PitCrew__Connector__RecoveryCommandTimeoutSeconds=120
PitCrew__Connector__RecoveryLedgerPath=/var/lib/pitcrew-connector/recovery-ledger
```

The allowlist, timeout, accepted command lifetime, and accepted observed-state
age are local policy. Dashboard administrators cannot expand any of them.

The ledger directory holds one durable record per command identifier and must
stay below the protected connector data root. The installer places it there and
the connector creates it with owner-only permissions on Unix hosts.

## Installation and upgrade

Recovery uses the same installer as capacity operations. Existing invocations
keep working unchanged and leave recovery disabled:

```powershell
./Enable-PitCrewCapacityOperations.ps1 `
    -Version 0.3.4 `
    -PitCrewRoot C:\dev\pitcrew `
    -DashboardUrl https://pitcrew.example.com `
    -Profiles copilot-cli `
    -CapacityMaximumCeiling 30
```

Opting in requires both the switch and an explicit recovery allowlist:

```powershell
./Enable-PitCrewCapacityOperations.ps1 `
    -Version 0.3.4 `
    -PitCrewRoot C:\dev\pitcrew `
    -DashboardUrl https://pitcrew.example.com `
    -Profiles copilot-cli `
    -CapacityMaximumCeiling 30 `
    -EnableManagerRecovery `
    -ManagerRecoveryProfiles copilot-cli
```

Supplying `-ManagerRecoveryProfiles` without `-EnableManagerRecovery` fails
before the installer touches the existing deployment, so an incomplete request
never silently changes operator policy. The recovery allowlist is independent of
the capacity allowlist; every listed profile must already exist below the PitCrew
state root.

Identity migration, protected service configuration, rolling logs, and rollback
to the read-only connector container on service startup failure are unchanged.

To revoke recovery, reinstall without `-EnableManagerRecovery`; to revoke all
host operations, remove the host service and restart the connector container.

## Capability advertisement

The connector advertises recovery for a profile only when every condition holds:

- the connector runs as the supported host service, not in a container;
- recovery is locally enabled and the profile is allowlisted;
- the configured PitCrew root and profile state resolve unambiguously;
- exactly one manager is running for the exact profile;
- the manager contract is 9 or newer;
- observed manager instance, generation, and hash are coherent and recent;
- no explicit manager shutdown request is present;
- no local profile operation is active.

A missing, stopped, or ambiguously matched manager is never remotely
recoverable. Starting a stopped manager or profile is out of scope.

## Dashboard workflow

Recovery appears only on profile detail, below the capacity summary. The control
is enabled only while the current connector capability proves every condition
the dashboard also enforces when queueing:

- the profile advertises host-operator recovery;
- local policy allows recovery for the profile;
- the manager contract is 9 or newer;
- exactly one running manager is locally resolved and currently running;
- the connector's observation and its last report are within the dashboard's
  accepted freshness window;
- no capacity or recovery operation is active for the profile;
- the viewer is a tenant administrator.

Every other state renders a specific explanation instead of the action:
read-only container connector, locally disallowed profile, stale observation,
stopped or missing manager, unresolved or duplicated managers, legacy manager
contract, active operation, revoked or offline connector, and insufficient
authorization. Non-administrators never see an enabled control, and the API
rejects their direct requests regardless of the browser.

Confirmation shows the tenant, node, and profile; the observed manager instance
and generation; the expected fences the request will carry; the configured,
target, local, and GitHub-eligible counts; the manager and autoscaling degraded
evidence currently available; the single manager-only restart that will happen;
the worker, Docker, host, capacity, image, release, routing, and configuration
changes that will not happen; and the possibility of a failed or indeterminate
outcome requiring local investigation. Confirmation requires an explicit
acknowledgement of the displayed fences. A refresh or capability change that
alters those fences closes the confirmation, discards the acknowledgement, and
reports that nothing was queued.

Progress and outcome are polled through the shared tenant fleet projection, so
recovery adds no separate polling loop. The profile shows queued, claimed,
started, succeeded, rejected, failed, expired, and indeterminate states with
their timestamps, the requesting administrator, and the bounded result detail.
Terminal rejected, failed, and indeterminate outcomes stay visible and never
become success-shaped, and the profile keeps a bounded immutable history of
earlier commands.

Evidence is reported as observation, never as proof of worker preservation: the
manager instance transition, generation and desired-state hash, observed-state
freshness against the accepted window, local and GitHub-eligible and target
counts, and the failure or rejection category are shown separately, alongside an
explicit statement that recovery issued no worker-directed mutation.

Incident alerts may link to this action only while the capability is currently
valid. No alert ever queues recovery automatically.

## Command lifecycle

1. A tenant administrator queues recovery with the fences the dashboard last
   observed.
2. The connector claims the command during its existing outbound sync.
3. Local state and capability are re-read immediately before the claim.
4. The command identifier, expected fences, locally resolved manager identity,
   and `started` intent are durably persisted before the recovery process runs.
5. PitCrew executes the recovery within the bounded local timeout.
6. The connector re-reads non-secret local state and reports `succeeded`,
   `rejected`, `failed`, or `indeterminate`.

The same command identifier is never executed twice. A redelivered command
replays the recorded outcome instead of invoking a second process.

## Interrupted attempts

If the connector stops after the durable `started` record but before an outcome,
it resolves the attempt on restart from the ledger and current local state:

- `succeeded` only when the fenced manager was provably replaced by a healthy,
  coherent instance;
- `failed` only when failure is provable;
- `indeterminate` otherwise.

An indeterminate attempt is never retried automatically. Inspect the profile and
queue a new command if recovery is still required.

## Mutual exclusion

One profile runs at most one local operation at a time. Recovery, capacity,
setup, refresh, and teardown cannot overlap; the connector honors both its own
local gate and PitCrew's profile lock, and reports `operation-active` instead of
queuing behind another operation.

## Excluded operations

Recovery never executes arbitrary command text or server-supplied paths, calls
Docker directly, starts a stopped manager or profile, touches workers, the
Docker engine, or the host, runs Compose down, prune, release update, capacity
change, or profile mutation, or transmits tokens, environment values, JIT
payloads, job output, or raw process error text.
