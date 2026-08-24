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

The portable, `containerized`, `windows-installed`, and `linux-installed`
profiles currently support:

- `topology-smoke-v1`
- `support-fresh-enrollment-diagnostic-v1`
- `support-diagnostic-mode-matrix-v1`
- `support-relay-restart-recovery-v1`
- `support-terminal-lifecycle-v1`

The portable and `containerized` profiles additionally support:

- `support-request-rejection-matrix-v1`

Dashboard's embedded support evidence policy pins the PitCrew commit accepted by
the broker. Selecting another PitCrew SHA intentionally fails before enrollment
rather than replacing the collector with a lookalike.

The Windows-installed profile runs only on a disposable standard
GitHub-hosted Windows runner. It packages the exact candidate source, installs
the agent and broker under separate Windows service identities, exercises the
same registered scenario, verifies the named-pipe/firewall/service boundary,
uses the typed enrollment-finalization action, and removes the complete
installation. It does not run on self-hosted capacity or a live PitCrew node.

The containerized profile runs only on disposable public Linux-hosted
infrastructure. It builds exact run-scoped Dashboard and relay images from the
selected source, verifies their content-addressed IDs and source labels, and
starts them with read-only roots, dropped capabilities, no-new-privileges,
bounded tmpfs mounts, Aspire session networking, and exact run-scoped data
volumes. The host-side agent and broker execute the same registered scenario
through loopback endpoints. Teardown uses recorded container IDs and exact
volume/image identities. This proves candidate container execution, not
production Compose, multi-architecture, registry, or physical-host behavior.

The Linux-installed profile runs only on a disposable standard GitHub-hosted
Ubuntu runner with passwordless administrative access. It packages the exact
candidate source for `linux-x64`, installs the agent and broker under separate
product users through the product installer, and exercises the same registered
scenario. Installer verification must prove systemd service definitions,
network isolation, Unix socket ownership/mode and peer credentials, exact
evidence ACLs, bootstrap finalization, and service health. Revocation and
DeleteKeys then remove the units, product users/groups, package roots, and
protected state. The canary refuses pre-existing product identities or paths,
copies the file-only PitCrew fixture to an exact run-scoped systemd-visible
root, proves both fixture copies unchanged, removes its exact fixture and
connector-health roots, and never runs on self-hosted capacity or a live
PitCrew node.

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

`support-relay-restart-recovery-v1` composes the fresh-enrollment workflow with
one typed AppHost control request after the first accepted poll. The AppHost
stops only its exact `support-relay` resource through Aspire's closed resource
command, holds the outage for 18 seconds so the existing 15-second agent poll
cadence crosses it, then starts the relay. The scenario requires the original
portable/container agent to remain alive, relay health, bootstrap
finalization, the second accepted poll, signed diagnostic completion,
revocation/DeleteKeys, and unchanged unrelated state. The control files contain
only run ID, request ID, operation, status, and bounded disposition; no generic
resource name or command crosses the scenario boundary.

`support-diagnostic-mode-matrix-v1` composes the same enrollment, finalization,
revocation, cleanup, and unchanged-state workflow without a topology-control
capability. It sequentially completes `ConnectorOffline`, `CapacityMismatch`,
`JobNotAssigned`, `HostPressure`, and `Full` through the actual PitCrew relay
verifier. Every mode must return a completed result with a valid node
attestation; the publishable evidence records only the bounded
`diagnostic-mode-matrix-verified` category rather than reports or mode-specific
host data.

`support-request-rejection-matrix-v1` runs only on portable and containerized
profiles. A run-scoped injector receives the ephemeral Dashboard authorization
key and relay-management bearer through process environment, then enqueues nine
closed request shapes through the real relay: malformed JSON, session mismatch,
wrong tenant/node, unsupported capability, unsupported diagnostic mode,
expired authorization, invalid nonce, a valid replay seed, and the repeated
nonce. The real agent must emit the expected bounded disposition for every
case, and the relay must make each rejection terminal before the agent advances.
The scenario then completes the normal signed diagnostic,
revocation/DeleteKeys, and unchanged-state proof.

The injector accepts no arbitrary envelope, URL, tenant, resource, command, or
diagnostic value. Its file contract contains only the run and control IDs,
closed case, relay session and enrolled node IDs, node public encryption key,
and optional replay group. Dashboard signing and relay-management secrets are
never persisted. The scenario does not run against `windows-installed` because
the canary deliberately does not bypass the installed service ACL to read the
node public-key descriptor. Broker report/markdown corruption remains a focused
agent boundary test: the hosted canary does not replace or fault-enable the real
broker to manufacture unsafe output.

`support-terminal-lifecycle-v1` composes the same active-node workflow and adds
one dormant, run-scoped support identity. A queued session is read before any
poll and then cancelled. A second dormant session expires under the canary's
30-second maximum lifetime. The dormant node then uses its real transport
credential to poll and report one closed rejection. Finally, the active
candidate agent completes a normal Dashboard-created session. The scenario
requires queued, cancelled, expired, dispatched, rejected, and completed state,
retained first-dispatch evidence, the bounded rejection disposition, and a
verified result before the base workflow performs its independent signed
diagnostic and cleanup.

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
the candidate Dashboard policy. Portable, containerized, and Linux-installed
jobs run independently on Ubuntu; Windows-installed runs on Windows. The
workflow uses no production credentials and never runs untrusted code through
`pull_request_target` or self-hosted capacity.

## Release gating

`.github/workflows/prepare-release.yml` is the only supported entry point for a
new Dashboard release. Dispatch it from `main` with the proposed version, the
current full Dashboard SHA, and the full PitCrew SHA pinned by the candidate
support evidence policy.

The workflow validates both repositories and the unused release tag, then runs
`support-fresh-enrollment-diagnostic-v1` through the reusable portable,
containerized, and Windows-installed canaries in parallel. `validate-only`
stops after that evidence. `create-draft` creates a GitHub draft only after all
three canaries succeed. A failed preflight or canary creates no tag or release.

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
minutes. Three pre-gate containerized runs completed in 3m25s, 3m09s, and
3m22s. The Windows-installed gate runs independently on `windows-latest` and
remains the measured critical path. These are observed operating times, not
timeout contracts. Release gating still does not claim Linux-installed,
multi-architecture registry, production Compose, or physical-host
qualification.

## Adding a scenario

Implement `ICanaryScenario`, declare its required capabilities, and add one
entry to `CanaryScenarioRegistry`. Do not add scenario-specific branches to
AppHost, lifecycle scripts, or the workflow. If a scenario needs a boundary the
active topology cannot provide, add a capability and topology adapter rather
than inferring support from the profile name.
