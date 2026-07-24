# Capacity operations

Protocol v3 adds one opt-in write capability: setting the absolute maximum for
an existing PitCrew profile with one unambiguous capacity target.

The dashboard does not receive a host path, repository URL, Docker socket, or
GitHub runner credential. The connector resolves every argument from local state
and invokes `Setup-Runner.ps1 -CapacityOnly`, preserving PitCrew's locking,
generation, and acknowledgement behavior.

## Deployment boundary

The existing connector container remains read-only. Capacity operations require
the same connector binary to run as a host service with:

- outbound HTTPS access to the dashboard;
- read access to the configured PitCrew state root;
- permission to execute the configured PitCrew checkout;
- `pwsh` and Docker CLI access required by `Setup-Runner.ps1`;
- access to the existing profile environment used by setup to validate its
  locally stored runner credential.

No additional container or inbound port is required.

Publish and run the existing connector application directly on the host:

```text
dotnet publish src/PitCrew.Connector.App -c Release -o /opt/pitcrew-connector
dotnet /opt/pitcrew-connector/PitCrew.Connector.App.dll
```

Use the host's normal service manager to supervise that process and persist its
existing identity path.

## Configuration

Operator mode is disabled by default. Configure the host process with values
equivalent to:

```json
{
  "PitCrew": {
    "Connector": {
      "OperatorModeEnabled": true,
      "PitCrewRoot": "/opt/pitcrew",
      "StateRoot": "/opt/pitcrew/.pitcrew-state",
      "AllowedCapacityProfiles": ["default", "copilot-cli"],
      "CapacityMaximumCeiling": 50,
      "CapacityCommandTimeoutSeconds": 300,
      "PowerShellExecutable": "pwsh"
    }
  }
}
```

Environment-variable equivalents use .NET's double-underscore convention, for
example:

```text
PitCrew__Connector__OperatorModeEnabled=true
PitCrew__Connector__PitCrewRoot=/opt/pitcrew
PitCrew__Connector__AllowedCapacityProfiles__0=default
PitCrew__Connector__CapacityMaximumCeiling=50
```

The profile allowlist and ceiling are local policy. Dashboard administrators
cannot expand either value.

## Supported profiles

This first protocol version advertises:

- repository profiles with exactly one existing repository target;
- organization or enterprise profiles with one shared replica target;
- the default profile and built-in profiles located under `profiles/`.

Multi-repository and external-manifest profiles remain read-only.

## Command lifecycle

1. A tenant administrator queues an absolute maximum.
2. SQLite records one active command for the node and profile.
3. The connector claims the command during its existing outbound sync.
4. Local policy and expected generation are checked again.
5. The connector invokes the setup script and verifies the resulting desired
   state.
6. The next sync reports `succeeded`, `rejected`, or `failed`.

Delivered commands may be offered again after the configured redelivery
interval. Re-execution is safe because the command contains an absolute value
and PitCrew setup converges idempotently.

## Excluded operations

The protocol does not support arbitrary commands, paths, profile creation or
deletion, routing changes, autoscaling policy changes, or release updates.
