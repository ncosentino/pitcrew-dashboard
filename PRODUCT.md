# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

The primary user operates one or more PitCrew GitHub Actions runner hosts. They use
the dashboard during routine fleet checks and during time-sensitive build, capacity,
connector, and host-pressure incidents.

Tenant administrators and system administrators are secondary users. They manage
tenant access, connector enrollment, diagnostic credentials, and other narrowly
authorized administrative tasks.

## Product Purpose

PitCrew Dashboard is an optional fleet control plane for PitCrew runner pools. It
helps operators, in this priority order:

1. detect what needs attention;
2. diagnose why it is happening;
3. identify the affected GitHub run or job;
4. safely pause, recover, or adjust capacity; and
5. manage enrollment, access, and diagnostic credentials.

Success means an operator can move from an operational signal to trustworthy evidence
and the appropriate safe action without guessing whether data is current, retained,
missing, or inferred.

## Positioning

PitCrew Dashboard is a read-only-by-default evidence plane that never invents
certainty. It combines outbound-only connector reporting with narrowly typed,
locally constrained operations instead of arbitrary remote administration.

The dashboard preserves the distinction between current, stale, last-known,
unavailable, and measured-zero evidence. It keeps workload cancellation and other
GitHub-owned actions in GitHub rather than acquiring broader write credentials.

## Operating Context

Operators work across tenant-scoped fleets of self-hosted GitHub Actions runner
hosts. They move between fleet triage, incident evidence, node and profile
investigation, runner or job correlation, retained history, and authorized
administration.

The product supports loopback and hosted dashboard deployments. Connectors
communicate outbound to the dashboard. Container connectors remain read-only and
socketless; separately installed host operators may execute only the typed operations
the local host permits.

## Capabilities and Constraints

- Browser authorization is presentation logic; server APIs remain the final tenant
  and role authority.
- Connector identity comes from credentials, never payload fields.
- The dashboard and connector never transmit or log runner credentials, connector
  identity material, JIT material, workload logs, or private host details.
- Missing evidence is never rendered as zero, and resource activity is never treated
  as proof of workload identity.
- Remote operations remain narrowly typed, fenced, expiring, auditable, and locally
  constrained. Arbitrary commands and server-supplied paths are prohibited.
- SQLite remains single-replica behind domain-specific storage interfaces unless
  measured evidence and an accepted architecture decision justify a change.
- Existing protocol-version compatibility and explicit unavailable states must be
  preserved.
- Incident acknowledgement records operator ownership without resolving the
  underlying condition. It remains immediately reversible until authoritative
  evidence resolves the incident.
- Product expansion must not weaken the outbound-only connector boundary or make the
  dashboard a general-purpose remote administration surface.

## Brand Commitments

PitCrew is the product name. The existing PitCrew mascot, logo, favicon, and social
preview are established product assets.

Product language is precise, calm, operational, and evidence-based. It names
uncertainty and prohibited effects directly rather than using reassuring but
unsupported language.

## Evidence on Hand

The product can present current and retained fleet, incident, connector-health,
hardware, capacity, resource, worker, GitHub job, recovery, and history evidence when
the relevant connector and manager contracts provide it.

Established brand assets live under `assets/`. The application, tests, protocol
schemas, architecture decisions, and maintained operational documentation are the
authoritative evidence for supported capabilities.

The repository contains no customer testimonials, commercial benchmarks, pricing, or
other marketing proof that future work may invent.

## Product Principles

1. **Truth before completeness.** Show what is known, when it was observed, and what
   is unavailable.
2. **Attention before inventory.** Lead operators to material exceptions before the
   complete fleet record.
3. **Evidence before action.** Connect operational state to its source and effect
   before offering a mutation.
4. **Narrow authority.** Keep actions typed, fenced, auditable, and owned by the
   system that can perform them safely.
5. **Fast investigation.** Preserve direct paths from fleet signal to node, profile,
   runner, job, incident, and retained evidence.

## Accessibility & Inclusion

The web interface targets WCAG 2.2 AA. Primary workflows must remain operable with
keyboard and assistive technology, at 200% zoom, with reduced motion, across light and
dark themes, and on narrow screens. Long identifiers, expanded translations, CJK,
emoji, and right-to-left text must not destroy task order or viewport containment.
