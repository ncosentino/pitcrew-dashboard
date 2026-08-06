# Capacity operations

Protocol v3 adds one opt-in write capability: setting the absolute maximum for
an existing PitCrew profile with one unambiguous capacity target. Protocol v9
allows that typed operation to request zero only when the host connector
advertises PitCrew manager contract 17 pause support.

The dashboard does not receive a host path, repository URL, Docker socket, or
GitHub runner credential. The connector resolves every argument from local state
and invokes `Setup-Runner.ps1 -CapacityOnly`, preserving PitCrew's locking,
generation, and acknowledgement behavior.

For a zero request, the connector invokes the explicit
`Setup-Runner.ps1 -Pause` path. The profile and manager remain present, busy
workers drain normally, and no replacement or new worker is admitted.

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

Dashboard releases publish self-contained `linux-x64`, `linux-arm64`,
`win-x64`, and `win-arm64` connector archives plus
`Enable-PitCrewCapacityOperations.ps1`. The installer:

1. locates the exact running Compose connector;
2. downloads and verifies the release-pinned host binary;
3. stops the connector container;
4. migrates its identity without displaying it;
5. installs and starts `pitcrew-connector.service` on Linux or the
   `PitCrewConnector` Windows Service;
6. restores the original container if service startup fails.

The operational workflow is one installer invocation, normally driven by the
PitCrew Copilot operations skill:

```powershell
./Enable-PitCrewCapacityOperations.ps1 `
    -Version 0.3.4 `
    -PitCrewRoot C:\dev\pitcrew `
    -DashboardUrl https://pitcrew.example.com `
    -Profiles copilot-cli `
    -CapacityMaximumCeiling 30
```

On Linux, run the installer as root; the systemd service runs as the invoking
sudo user when available. On Windows, the installer requests UAC elevation when
needed and reports the elevated operation's actual result rather than trusting
the shell process exit code. The Windows Service runs as `LocalSystem`, stores
binaries below `C:\Program Files\PitCrew\Connector`, and stores its protected
identity below `C:\ProgramData\PitCrew\Connector`. Rolling service logs are
written below the same protected data directory.

`LocalSystem` is deliberate: executing `Setup-Runner.ps1 -CapacityOnly`
requires access to the Docker engine, which is already host-equivalent
privilege. The connector still accepts only the typed capacity command and
never receives a dashboard-supplied path, executable, or argument.

## Configuration

Operator mode is disabled by default. The installer writes host configuration
equivalent to:

```json
{
  "PitCrew": {
    "Connector": {
      "OperatorModeEnabled": true,
      "PitCrewRoot": "C:\\dev\\pitcrew",
      "StateRoot": "C:\\dev\\pitcrew\\.pitcrew-state",
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

Dashboard records the pre-pause maximum. Once zero is acknowledged, it offers
**Resume to N** only while the recorded pause generation still matches the
connector's current generation. An out-of-band change removes that shortcut;
an administrator must then choose an explicit positive maximum.

Pressure incidents may offer the same confirmed pause operation when the user
is an administrator and the host connector advertises support. Read-only
connectors show GitHub job links without pause controls, and incidents never
pause automatically.

## Excluded operations

The protocol does not support arbitrary commands, paths, profile creation or
deletion, routing changes, autoscaling policy changes, or release updates.

The dashboard also does not cancel GitHub jobs. Exact run/job links keep
cancellation authorization and audit in GitHub.

## Manager recovery

Protocol v4 adds a separately permissioned typed recovery operation that uses
the same host service and installer. It is disabled by default and never
enabled by upgrading a capacity-only host. See
[Manager recovery](manager-recovery.md).
