# Support-plane release rollout

This guide coordinates one released support-plane version across the hosted
Dashboard and relay, a PitCrew node, and the read-only diagnostics client. It
composes the existing hosted deployment, relay, installer, and remote-diagnostics
procedures; it does not add another lifecycle implementation.

## Qualified release pair

The current release pair is:

| Surface | Required release | Verified identity |
| --- | --- | --- |
| PitCrew node and diagnostics client | `v0.10.8` | commit `a9fc5884b7e1aea6ef731c701401c46a51d0d3f5` |
| Dashboard, support relay, agent, broker, and installer | `v0.12.21` | exact commit referenced by the published tag |
| Support evidence policy | schema 2 | `support-evidence-policy-v0.10.8.json` |
| Diagnostics collector | UTF-8/LF SHA-256 | `18ed0cdb53e288f981bf5cc49cb404a5129b98ac14faaa5a6cbcab07b3591580` |

The raw `v0.10.8` collector release asset has SHA-256
`c4cf8df68a3ce2402300010deb68c3bde92247e6b4c15ddfafc223ff52c85f11`.
The installer normalizes UTF-8 line endings to LF before enforcing the policy
hash, so Windows and Linux checkouts produce the same contract identity.

This table names the pair exercised by the portable, containerized,
Windows-installed, and Linux-installed canaries. Do not infer compatibility for
another PitCrew commit or mix Dashboard, relay, agent, broker, and installer
versions. Dashboard `v0.12.20` remains paired with the older PitCrew `v0.10.3`
policy.

## Release-integrity preflight

Complete this phase before accessing either host.

1. Resolve the full commit referenced by Dashboard tag `v0.12.21`.
2. Dispatch the read-only `Verify published release` workflow with that tag and
   commit:

   ```powershell
   gh workflow run verify-published-release.yml `
       --repo ncosentino/pitcrew-dashboard `
       --ref main `
       -f release_tag=v0.12.21 `
       -f release_sha=<full-release-sha>
   ```

3. Require all five jobs to pass: release identity, the complete asset
   inventory, and Dashboard, connector, and support-relay image
   index/provenance verification.
4. Verify PitCrew tag `v0.10.8` resolves to
   `a9fc5884b7e1aea6ef731c701401c46a51d0d3f5`.
5. Require the PitCrew release to contain the collector, broker-access policy,
   broker-access schema, and all three SHA-256 sidecars. Reject a missing,
   duplicate, or mismatched asset.
6. Require exactly one support release-gate marker in the Dashboard release and
   a successful preparation run at the release SHA with portable,
   containerized, Windows-installed, Linux-installed, and draft-creation jobs.

The Dashboard release gate binds the package to the PitCrew commit above and
runs the same scenario implementation on all four topology profiles. A
successful package download alone is not compatibility evidence.

## Target preflight

Record the current versions, service states, and exact rollback targets without
displaying configuration, identities, enrollment material, or credentials.

Hosted preflight:

- resolve one Compose project and exactly one active ingress adapter;
- include the optional support-relay overlays in every Compose command;
- read only the independently pinned Dashboard and relay version lines;
- validate the complete model with `config --quiet`;
- verify both current SQLite databases are healthy; and
- retain the current image versions and exact backup destinations.

Node preflight:

- require a clean PitCrew deployment checkout or other supported release
  installation at `v0.10.8`;
- resolve each selected profile from local configuration, not a server-supplied
  path;
- require fresh `support-evidence` projections for every selected profile;
- require the fixed collector and canonical hash;
- record the current support installer version and whether one rollback version
  is retained; and
- stop if installer failure diagnostics or `Verify` report a degraded boundary.

Updating PitCrew uses the published `pitcrew-pool-update` workflow. It must
complete before installing or updating the broker so the exact evidence
directories and collector already exist. Compatible manager handoff preserves
workers and active jobs; stop after the first profile failure rather than
concealing partial completion.

## Staged rollout

Apply one boundary at a time.

### 1. Hosted relay and Dashboard

Follow the complete [hosted support relay](support-relay.md) and
[hosted deployment](../hosted-deployment.md) procedures.

1. Pre-pull only the `0.12.21` Dashboard and support-relay images with
   process-scoped version overrides.
2. Stop the complete scoped model.
3. Create and verify independent timestamped backups of the Dashboard and relay
   SQLite databases while the old versions remain pinned.
4. Replace only the two version lines.
5. Start and verify the relay privately.
6. Start and verify Dashboard privately, including the exact hosted-ingress
   contract.
7. Start the complete model and verify both public origins.

Do not enroll or update a node until both origins are healthy. Never use
`docker compose down`, restart Docker, expose relay storage, or replace the
scoped services with standalone containers.

### 2. PitCrew node

After PitCrew `v0.10.8` is verified, download the `v0.12.21` installer archive
and sidecar for the node's exact RID. Extract the package and run only its
bundled installer.

For an existing managed support installation:

```powershell
./Install-PitCrewSupportPlane.ps1 `
    -Action Update `
    -Version 0.12.21 `
    -AllowMachineChanges

./Install-PitCrewSupportPlane.ps1 -Action Verify
```

For a first installation, create one tenant-bound enrollment authorization,
write the bounded agent settings through the supported enrollment workflow, and
run:

```powershell
./Install-PitCrewSupportPlane.ps1 `
    -Action Install `
    -Version 0.12.21 `
    -PitCrewRoot <pitcrew-root> `
    -Profiles <profile-id> `
    -AgentSettingsPath <protected-agent-settings> `
    -AllowMachineChanges
```

Require separate agent and broker identities, exact IPC and evidence ACLs,
broker network isolation, running services, and the first accepted relay poll.
Then remove bootstrap material only through:

```powershell
./Install-PitCrewSupportPlane.ps1 `
    -Action FinalizeEnrollment `
    -AllowMachineChanges
```

Finalization must preserve the broker process, restart only the agent, and
produce a second accepted poll.

## Rollback boundaries

Before public ingress activation, a failed hosted update may restore the
previous version lines and both verified pre-update databases. After public
ingress activation, preserve the migrated databases: clients may have written
new state, so an automatic snapshot restore is unsafe.

A failed node binary update restores its previous version automatically when
service startup fails. For a later operator-approved rollback, use the bundled
installer's `Rollback` action and then `Verify`; never copy binaries or rewrite
service definitions manually. The installer retains only one previous version.

If PitCrew itself must return to `v0.10.3`, first roll the support package back
to `v0.12.20` so its embedded evidence policy matches. Replay the exact prior
PitCrew setup commands through `pitcrew-pool-update`. Do not combine the
Dashboard, relay, node-package, and PitCrew rollback into one unbounded action.

Once a diagnostic request or result has crossed the new hosted boundary, report
partial completion and diagnose it. Do not hide it behind a successful rollback
of another component.

## Dashboard-only acceptance

After the node reports two accepted polls, acceptance requires no shell, remote
desktop, Docker socket, host logs, or direct node filesystem access.

From an authenticated Dashboard administrator session:

1. Require the support identities and sessions endpoints to return HTTP 200.
2. Verify exactly the intended support identity is enabled and its relay poll
   timestamp is newer than the rollout.
3. Create one bounded `Full` diagnostic session for an approved local profile.
4. Observe an explicit queued, dispatched, and completed lifecycle before
   expiry.
5. Require the completed result to retain its capability, request digest,
   expiry, and node signing-key fingerprint.
6. Require successful node-signature and package verification before treating
   the report as evidence.
7. Confirm connector state is neither changed nor used as proof of support-agent
   health.

Stop on an unavailable, stale, rejected, expired, or mismatched result. Do not
create another identity or session merely because the first outcome is
inconvenient.

The release pair is accepted for rollout after this check. The stronger V1 exit
evidence still requires deliberate target-side connector-offline,
disconnect/reconnect, relay-restart, disable/enable, and
uninstall/preserve/reinstall exercises. Those scenarios remain separate because
Dashboard-only observation cannot prove host lifecycle or isolation behavior.
