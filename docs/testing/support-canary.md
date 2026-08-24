# Cross-repository support canary

The support canary runs actual candidate Dashboard, relay, agent, broker, and
PitCrew diagnostics code against an isolated Aspire topology. It is the
cross-repository compatibility gate; it does not replace container, installer,
security-boundary, or physical-host validation.

## Inputs

Every run requires:

- a clean `ncosentino/pitcrew-dashboard` checkout at one full commit SHA;
- a clean `ncosentino/pitcrew` checkout at one full commit SHA;
- one registered scenario; and
- one topology profile.

The portable and `windows-installed` profiles currently support:

- `topology-smoke-v1`
- `support-fresh-enrollment-diagnostic-v1`

Dashboard's embedded support evidence policy pins the PitCrew commit accepted by
the broker. Selecting another PitCrew SHA intentionally fails before enrollment
rather than replacing the collector with a lookalike.

The Windows-installed profile runs only on a disposable standard
GitHub-hosted Windows runner. It packages the exact candidate source, installs
the agent and broker under separate Windows service identities, exercises the
same registered scenario, verifies the named-pipe/firewall/service boundary,
uses the typed enrollment-finalization action, and removes the complete
installation. It does not run on self-hosted capacity or a live PitCrew node.

## One-command run

From the Dashboard checkout:

```powershell
./scripts/canary/Invoke-SupportCanary.ps1 -DashboardSourceRoot <dashboard-checkout> -DashboardCommit <dashboard-sha> -PitCrewSourceRoot <pitcrew-checkout> -PitCrewCommit <pitcrew-sha> -OutputRoot <run-output-directory> -Scenario support-fresh-enrollment-diagnostic-v1 -TopologyProfile portable -Configuration Release
```

The command builds candidates, starts the topology, runs the scenario, and
stops the exact run in a `finally` boundary.

## Layered commands

Use the individual scripts when developing topology or scenarios:

1. `Resolve-SupportCanarySources.ps1` creates detached public checkouts.
2. `New-SupportCanaryRun.ps1` verifies SHAs and creates `plan.json`.
3. `Build-SupportCanary.ps1` builds the exact candidate applications.
4. `Start-SupportCanaryTopology.ps1` starts Aspire and waits for
   `runtime.json`.
5. `Invoke-SupportCanaryScenario.ps1` attaches the standalone runner.
6. `Stop-SupportCanaryTopology.ps1` uses the run ID, PID, and start-time fence
   for exact teardown.

Scaffolding and topology execution are independent: a run can be inspected after
scaffolding, and multiple registered scenarios can attach to a compatible
running topology without changing AppHost orchestration.

## Evidence and secrets

`plan.json` and `runtime.json` are non-secret. The only publishable artifact is
`evidence/<scenario-id>.json`, which contains stable steps, bounded outcome
categories, durations, and timestamps.

Do not publish the run root. It contains ephemeral enrollment settings,
identity keys, SQLite databases, topology logs, and candidate-local paths. The
relay-management bearer and diagnostic credential exist only in child-process
environment and never appear in a manifest or command argument.

## CI

`.github/workflows/support-canary.yml` is reusable and manually dispatchable.
It accepts exact Dashboard and PitCrew SHAs, scenario ID, and topology profile.
Pull requests use public GitHub-hosted Ubuntu and the PitCrew commit pinned by
the candidate Dashboard policy. The workflow uses no production credentials and
never runs untrusted code through `pull_request_target` or self-hosted capacity.

## Release gating

`.github/workflows/prepare-release.yml` is the only supported entry point for a
new Dashboard release. Dispatch it from `main` with the proposed version, the
current full Dashboard SHA, and the full PitCrew SHA pinned by the candidate
support evidence policy.

The workflow validates both repositories and the unused release tag, then runs
`support-fresh-enrollment-diagnostic-v1` through the reusable portable canary.
`validate-only` stops after that evidence. `create-draft` creates a GitHub draft
only after the canary succeeds. A failed preflight or canary creates no tag or
release.

The draft contains generated notes plus one bounded gate marker tied to the
preparation workflow run. Wait for the preparation workflow to complete
successfully, edit the human-facing generated notes without removing the HTML
marker, and publish the draft. Container, connector, and support-plane
publishers independently resolve the marker to the successful same-commit
workflow run, require its draft-creation job, and verify its PitCrew SHA against
the released Dashboard policy. Publishing the draft through GitHub emits the
release event that starts those publisher workflows. Missing, duplicate,
altered, failed, or stale evidence blocks every publisher.

Manual `Publish support plane` dispatch remains a package-only development
path. It does not require release evidence because its release-upload step is
disabled. It cannot be used to bypass the published-release gate.

The portable gate has completed on GitHub-hosted Ubuntu in approximately two
minutes. The Windows-installed gate runs independently on `windows-latest`; its
measured duration is recorded by each release preparation run. Those are
observed operating times, not timeout contracts. Release gating still does not
claim Linux-installed, containerized, or physical-host qualification.

## Adding a scenario

Implement `ICanaryScenario`, declare its required capabilities, and add one
entry to `CanaryScenarioRegistry`. Do not add scenario-specific branches to
AppHost, lifecycle scripts, or the workflow. If a scenario needs a boundary the
active topology cannot provide, add a capability and topology adapter rather
than inferring support from the profile name.
