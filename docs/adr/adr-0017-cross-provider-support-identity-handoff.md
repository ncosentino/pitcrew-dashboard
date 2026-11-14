---
title: "ADR-0017: Cross-provider support identity handoff"
status: "Accepted"
date: "2026-11-14"
authors: ["Nick Cosentino"]
tags: ["architecture", "support", "identity", "migration"]
supersedes: ""
superseded_by: ""
---

# Context and scope

Windows support identities use user-scoped, persisted CNG keys with private
export disabled. The owning managed agent service can use those keys, but an
administrator launching an ordinary process cannot substitute for that service
account. Native Linux uses the existing owner-only file provider.

Verified facts: existing Dashboard rotation authenticates the old transport
credential, prepares exact replacement public keys/credential, rejects active
queued/dispatched sessions, blocks new sessions while prepared, and durably
finalizes Dashboard and relay promotion. Local rotation and polling share the
identity-operation lock. The existing installer can run fixed lifecycle requests
through the owning service and already owns platform identities, evidence ACLs,
broker network isolation, and trusted IPC.

Assumption: an explicitly privileged administrator controls both installations
and performs a quiesced opaque file transfer. A local acknowledgement cannot
prove a remote host's actual service state; it is an explicit administrator
trust assertion, not a new remote authority.

# Decision drivers

Preserve the same node, tenant, policies, trust and generation history without
exporting CNG private keys or enrolling a competing identity. Prevent automatic
second authority after uncertain network or filesystem outcomes. Preserve
terminal cached signatures and replay tombstones. Reuse the existing rotation,
providers, and managed-service boundary instead of a command bus.

# Decision

Use four typed installer lifecycle actions: source public offer, native target
staging, source preparation/quiescence, and native activation. Requests use fixed
filenames and a closed operation enum; no request supplies a path, executable,
script, or network destination.

The target generates replacement keys and credential through the Linux provider.
Its signed authorization binds the offer digest, rotation ID, both replacement
public keys, and credential, encrypted to the source CNG RSA public key. The
owning Windows agent service decrypts/verifies that authorization and uses
existing Dashboard preparation with its original credential.

The source durably fences polling before preparation. Under the shared lock it
retains original CNG keys and captures fixed replay stores. A source-CNG-signed,
target-encrypted transfer binds exact preparation and replay state. The installer
publishes quiescence acknowledgement only after both source services are verified
stopped/disabled. The administrator transfers the opaque files explicitly.

The target accepts only the pinned source signature and exact staged request,
preserves trust metadata, restores validated replay entries without replacing
conflicting state, commits locally, and finalizes the existing rotation. Target
polling stays fenced until finalization. Recovery repeats the identical durable
operation; it never compensates by enabling the old source.

Cached result envelopes remain signed by their original key and encrypted for
Dashboard. Returning an already sealed cached terminal result by authenticated
poll session ID requires no retired request-decryption key and no broker rerun.
New sessions use replacement keys; existing terminal Dashboard results and
attestations are unchanged.

The exchange is bounded local installation state, not a remote operations
capability or private-key export format. Broker IPC, network restrictions and
the existing four-file evidence ACL/publication contract are unchanged.

# Alternatives

Exporting source private keys weakens the CNG contract and is rejected.
Reenrollment creates competing identity/policy history and is rejected.
Launching rotation as an administrator cannot use user-scoped virtual-account
CNG keys and is rejected. A generic service command, arbitrary path receiver,
or direct Windows-service access to a Linux/WSL root unnecessarily broadens the
managed boundary and is rejected.

# Consequences

The old source remains recoverable but deliberately fenced; automatic rollback
is unavailable after uncertain preparation. The operator must explicitly drain
active sessions and own the final quiescence/transfer. Oversized or invalid
replay stores fail without truncation. Both installations need migration-capable
packages, including updating an older source under its unchanged service account.

# Confirmation

Focused tests must cover real non-exportable CNG preparation, target-key
substitution rejection, source-fence/no-reenrollment behavior, exact retry
deduplication, acknowledgement-required activation, retained source keys,
same-node promotion, unchanged cached outcomes, and replay path/conflict
rejection. Remote platform/installer checks must preserve service-account
separation, effective network and IPC restrictions, and bounded evidence ACLs.
PR readiness and release remain policy gated, including trusted current-head
human approval.

# References

- [ADR-0008](adr-0008-support-plane-v1-read-only-diagnostics.md) owns diagnostics,
  provider protections and split transport/broker boundaries.
- [Support plane](../support-plane.md#windows-service-to-linux-identity-migration)
  owns executable installation, migration, recovery and acceptance procedures.
- `SupportIdentityMigration` binds target authorization and source transfer to
  existing rotation; `SupportNodeIdentityStore` provides cross-process fencing.
- `SqliteSupportStore.GetIdentityRotationStatusAsync` demonstrates the active
  session gate and exact retry authorization; existing installer helpers own
  service lifecycle, evidence ACLs and IPC trust.
