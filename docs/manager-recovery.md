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
