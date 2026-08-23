---
title: "ADR-0009: Trusted outbound image candidate orchestration"
status: "Accepted"
date: "2026-08-23"
authors: ["Nick Cosentino"]
tags: ["architecture", "github", "images", "security", "storage"]
supersedes: ""
superseded_by: ""
---

# Context and scope

PitCrew can produce a bounded, versioned image-candidate report containing an
immutable digest, source identity, workflow run identity, qualification results,
and closed failure categories. Dashboard does not yet have a trusted way to
request that workflow, correlate the resulting GitHub run, retain the candidate,
or show deterministic failures to the requesting operator.

This decision governs tenant-scoped registration and execution of trusted image
workflows, GitHub authentication, run and artifact correlation, candidate
lifecycle, and bounded SQLite persistence. It does not authorize Dashboard to
build images, access Docker, accept arbitrary workflow dispatches, roll images
out to hosts, target fleets, or execute an LLM-driven workflow.

The existing browser login uses a GitHub OAuth App and deliberately does not
save user access tokens. Background image orchestration therefore requires a
separate application identity rather than broadening interactive user
credentials.

# Verified facts and assumptions

Verified facts:

- Dashboard supports both loopback and hosted deployments, so a required
  inbound webhook or callback would exclude a supported operating mode.
- The GitHub REST workflow-dispatch endpoint accepts a branch or tag ref,
  bounded declared inputs, and returns the exact workflow run identifier and
  URLs.
- GitHub App installation tokens can be restricted to selected repositories
  and permissions and expire after one hour.
- GitHub exposes artifacts for one exact workflow run, including artifact size,
  digest, expiry, and associated run identity.
- PitCrew candidate report version 1 is bounded and distinguishes ready and
  failed outcomes without raw build logs or credentials.
- Dashboard keeps SQLite behind domain-specific feature interfaces and remains
  single-replica.

Assumptions to confirm during implementation:

- GitHub App installation authentication remains available for every REST
  endpoint used by the registered-workflow contract.
- GitHub preserves the dispatch response containing the exact run identifier
  for the pinned API version used by Dashboard.
- Candidate-producing repositories can adopt the fixed reserved inputs and
  artifact shape without embedding credentials in workflow inputs.

# Decision drivers

- Work in both loopback and hosted deployments without adding inbound
  reachability.
- Never persist user OAuth tokens, installation tokens, workflow tokens,
  registry credentials, or GitHub App private-key material in SQLite.
- Prevent arbitrary repository, workflow, ref, path, and input dispatch.
- Correlate each request to one exact repository, workflow revision, source
  commit, GitHub run, artifact, and candidate report.
- Preserve deterministic, restart-safe, idempotent state transitions.
- Store bounded structured evidence rather than workflow logs.
- Keep tenant authorization, antiforgery, rate limiting, and audit identity
  enforceable at the server boundary.
- Keep the image-build plane separate from later host and fleet rollout
  authority.

# Decision

Dashboard will add a separate image-orchestration feature boundary backed by a
GitHub App installation client and a domain-specific candidate store.

## GitHub application identity

The deployment configures one GitHub App identifier and private key through the
deployment secret boundary. Private-key bytes are loaded only by the GitHub
adapter and are never returned by APIs, written to SQLite, or included in logs.

Each API operation creates or reuses a short-lived installation token restricted
to the registered repository and the minimum permissions required by the exact
endpoint. Tokens remain in memory only, expire no later than GitHub's expiry,
and are discarded early enough to avoid use near expiry.

The initial permission set is:

- Actions read and write for workflow dispatch, run status, and artifacts;
- Contents read for workflow and source-revision validation;
- Metadata read as the GitHub App baseline permission.

The existing GitHub OAuth App remains browser-authentication-only.

## Immutable workflow registration

A tenant administrator registers an image recipe only after Dashboard validates
the installation and repository. Each registration version freezes:

- GitHub installation, repository numeric identity, and canonical owner/name;
- workflow numeric identity and path;
- workflow file blob identity at one fixed branch or tag dispatch ref;
- recipe identifier and supported candidate schema version;
- allowed source-ref policy;
- a bounded schema for non-secret workflow inputs.

Registration reads and validates the workflow definition at the selected ref.
Dispatch is rejected if the workflow is disabled, missing, renamed, outside the
installation, or no longer matches the reviewed blob identity. Changing any
frozen value creates a new registration version; existing requests continue to
reference the version they used. A registration may be disabled without
rewriting its audit history.

Input schemas declare exact keys, primitive types, required values, maximum
lengths, and optional closed enums. Secret-shaped keys and values are
prohibited. A build request supplies values only for those declared keys.
Dashboard never accepts an arbitrary input map.

Every registered workflow also accepts reserved Dashboard-owned inputs:

- a globally unique request identifier;
- the exact source commit;
- the recipe identifier.

The workflow definition ref and source commit are separate. GitHub executes the
reviewed workflow from its fixed branch or tag, while the workflow builds the
exact validated source commit passed by Dashboard.

## Dispatch and exact run correlation

Dashboard persists the request before contacting GitHub. It then verifies that
the source commit belongs to the registered repository and satisfies the
allowed source-ref policy.

Dashboard dispatches only the frozen workflow identity at the frozen ref, using
the reserved inputs plus validated declared inputs. Dashboard pins the GitHub
REST API version whose dispatch response supplies the exact workflow run
identifier and URLs. Dashboard stores that run identifier atomically and never
discovers a run by time window, title, branch guess, or log content. A response
without an exact run identifier blocks the request and prevents further
dispatch until the integration contract is restored.

A bounded background worker polls only stored active run identifiers. Primary
or secondary GitHub rate limits, transport failures, and GitHub unavailability
retain the current lifecycle state with bounded retry evidence; they do not
invent a workflow failure.

## Candidate artifact contract

After the exact run reaches a terminal conclusion, Dashboard queries artifacts
for that run only. Version 1 requires exactly one unexpired artifact named
`pitcrew-image-candidate` containing exactly one bounded
`image-candidate.json` file.

Dashboard rejects archives with extra files, links, path traversal, unsupported
compression, excessive compressed or expanded size, invalid UTF-8, or an
unsupported schema version. It validates the report before persistence and
requires exact agreement with the stored tenant request:

- recipe identifier;
- source repository and commit;
- workflow run identifier;
- supported platform and output mode;
- immutable digest and reference invariants;
- complete closed qualification set;
- ready or failed status invariants.

Artifact metadata, archive digest, external run URL, and validated candidate
fields are retained. Raw workflow logs, archive bytes, credentials, environment
values, and unbounded error text are not retained.

## Lifecycle and idempotency

The durable build-request lifecycle is:

```text
requested -> dispatching -> building -> qualifying -> ready
                                                 -> blocked
                                                 -> failed
```

Transitions are monotonic and keyed by the Dashboard request identifier. The
GitHub run identifier becomes immutable once recorded. Repeated dispatch
responses, polling results, artifact downloads, and candidate ingestion converge
without duplicate candidates or qualification rows.

`ready` requires a schema-valid ready report with an immutable digest and every
required qualification passed. `failed` represents a terminal trusted workflow
or candidate failure. `blocked` represents a policy, identity, artifact, or
validation mismatch that requires administrator intervention. Temporary GitHub
unavailability remains retryable and does not become either terminal state.

A candidate is created only from a schema-valid report whose request and GitHub
identity agree with stored authority. A workflow that terminates without the
required artifact fails the request with a bounded category but does not create
a success-shaped candidate. Ready and failed candidate reports create immutable
candidate records; blocked or malformed evidence remains attached to the
request as bounded validation evidence.

Terminal requests and candidates are immutable. A retry creates a new request
and, when valid report evidence exists, a new candidate rather than rewriting
prior evidence.

## Authorization, persistence, and retention

Tenant administrators may register or disable recipes and request builds.
Tenant members may read candidate state according to the existing tenant
authorization model. Every mutation uses authenticated GitHub user identity,
antiforgery protection, and a tenant-scoped rate limit.

SQLite stores versioned recipe registrations, build requests, exact GitHub run
identity and status, immutable candidates, and bounded qualification evidence
behind an image-feature storage interface. Active polling claims are
transactional so restart or overlapping worker ticks cannot process one request
concurrently.

Retention is bounded by both age and count while preserving active requests.
Immutable audit fields include the requester, registration version, source
commit, normalized input values, timestamps, run identity, artifact identity,
and terminal result. External links are presentation evidence, not authority.

# Alternatives considered

## Receive an OIDC-authenticated workflow callback

GitHub OIDC could strongly bind repository, workflow, ref, commit, and run
claims while avoiding a stored callback secret. It would provide lower latency
and fewer polling requests.

It is rejected for version 1 because it requires an inbound public endpoint,
nonce and replay handling, and a second delivery protocol that loopback
deployments cannot receive. Exact run IDs from workflow dispatch make that
complexity unnecessary.

## Consume GitHub webhooks

Webhooks provide prompt status changes and established redelivery identifiers.
They still require inbound reachability, webhook-secret lifecycle, delivery
retention, duplicate handling, and tenant installation routing. Polling exact
run IDs has higher latency but one outbound trust boundary and works in every
supported deployment.

## Use user OAuth tokens or personal access tokens

User tokens would avoid a separate GitHub App setup. They also tie background
work to a person's session, revocation, and broad repository grants. The current
authentication boundary intentionally does not save OAuth tokens. This option is
rejected.

## Offer a generic workflow-dispatch API

A generic dispatcher would be reusable for other automation. It would also
permit caller-selected repositories, workflows, refs, and inputs, creating an
automation-control surface much broader than image candidates. It is rejected.

## Require manual artifact upload

Manual upload would reduce GitHub integration work, but it preserves the human
relay this feature exists to remove and weakens run correlation. It is rejected.

# Consequences

Dashboard gains a deterministic image-candidate lifecycle without Docker access,
host authority, or inbound callbacks. Loopback and hosted installations use the
same flow. Candidate failures can be shown directly to the requester after
GitHub publishes terminal evidence.

Operators must install and configure a GitHub App and explicitly register each
workflow revision. Workflow changes require a new reviewed registration version.
This adds administration work but prevents silent expansion of automation
authority.

Polling introduces bounded latency and consumes GitHub API quota. Rate-limit
handling and active-request batching become operational concerns. GitHub
unavailability can delay truth but cannot be represented as a build failure.

The trusted repository workflow remains part of the security boundary. Dashboard
can prove which reviewed workflow revision and source commit were requested and
can validate the resulting report; it cannot prove that arbitrary workflow code
implemented the intended recipe beyond the qualifications that workflow emits.

The new domain adds durable state and migrations but does not require another
database, broker, cache, or Dashboard replica.

# Confirmation

Architecture compliance is confirmed by tests that prove:

- installation tokens are repository- and permission-restricted, short-lived,
  absent from persistence, responses, and logs;
- registration rejects changed workflow blobs, disabled workflows, undeclared
  inputs, secret-shaped inputs, invalid source refs, and cross-installation
  repositories;
- dispatch persists and uses the exact returned run identifier;
- polling is restart-safe, rate-limit-aware, bounded, and single-claim;
- tenant authorization, antiforgery, and endpoint rate limits apply to every
  mutation;
- artifact extraction rejects traversal, links, extra files, oversize content,
  invalid UTF-8, and unsupported schemas;
- candidate validation requires exact request, run, repository, commit, recipe,
  digest, and qualification agreement;
- repeated status and artifact observations are idempotent;
- temporary GitHub failure remains retryable while terminal workflow and
  candidate failures remain distinct;
- SQLite retention is bounded and preserves active requests.

One hosted integration test dispatches a disposable registered workflow, records
the exact returned run ID, downloads its fixed candidate artifact, and persists
one ready or failed candidate without exposing credentials or logs.

# References

- `PRODUCT.md` establishes loopback and hosted deployments, narrow authority,
  truthful unavailable state, and SQLite single-replica constraints.
- `DashboardAuthenticationPlugin` configures GitHub OAuth without saving user
  tokens, demonstrating why background execution needs a separate application
  identity.
- `docs/architecture/data-access.md` requires domain-owned persistence
  interfaces and explicit transaction boundaries.
- [PitCrew image candidate schema](https://github.com/ncosentino/pitcrew/blob/main/image-candidate.schema.json)
  defines the bounded producer contract Dashboard will validate.
- [GitHub workflow dispatch REST API](https://docs.github.com/en/rest/actions/workflows#create-a-workflow-dispatch-event)
  documents branch-or-tag refs, declared inputs, and the exact returned workflow
  run identity used for correlation.
- [GitHub App installation authentication](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation)
  documents repository and permission restriction plus one-hour installation
  token expiry.
- [GitHub Actions artifact REST API](https://docs.github.com/en/rest/actions/artifacts)
  documents exact-run artifact listing, metadata, digest, expiry, and bounded
  archive download.
- [Issue #150](https://github.com/ncosentino/pitcrew-dashboard/issues/150)
  owns implementation scope and delivery history.
