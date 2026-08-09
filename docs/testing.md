# Testing strategy

This template treats tests as executable architecture evidence. A passing suite should
prove the generated dependency graph, request boundary, persistence behavior, and
failure contracts rather than only exercise isolated methods.

## Feedback layers

- **Focused logic tests** cover deterministic pure behavior.
- **Service tests** resolve the real service graph and replace only true external
  boundaries.
- **Repository and API tests** use the production database engine and real generated
  registration.
- **Template gates** scaffold representative symbol/component combinations before
  application tests run.

Choose the cheapest layer that can prove the behavior without substituting the
contract under test.

## Development loop

For new behavior:

1. Add one failing behavior test.
2. Make the smallest production change that passes.
3. Refactor only while the test remains green.

When changing unfamiliar behavior, add characterization tests first. They describe the
current contract; change their expected result only when the behavior change is
intentional and reviewed.

Regression tests must fail when the defect is reintroduced. A test written only after
the fix, without proving that failure, is weaker evidence.

## Boundaries and data

Keep same-domain code real. Mock only a system, vendor, process, or cross-feature
boundary that the test cannot run directly.

Data-access behavior uses the production database engine. In-memory substitutes and
repository mocks can pass while real queries, transactions, collation, or locking fail.
Use unique data per test so parallel execution remains isolated.

Time-dependent behavior uses `TimeProvider` and an explicitly seeded fake clock.
Advance simulated time only after the system under test has registered the timer it is
waiting on.

## Assertion quality

Assert exact outcomes and collection counts. Use the test framework's dedicated
exception and result assertions rather than decomposing one contract into unrelated
boolean/value/error checks.

Generated TUnit projects treat assertion analyzers as build errors. Await every
assertion and assert values computed by the system under test.

## Mutation testing

Mutation testing is useful for pure, high-risk changed code because it tests whether
assertions can detect plausible defects. It is not a default local or PR gate in this
pilot:

- the default scaffold adds zero mutation packages and zero mutation CI jobs;
- it adds material runtime;
- generated projects differ in pure-code surface;
- survivors require triage rather than an automatic threshold guess.

Run a focused report at PR readiness when the changed scope justifies the cost. Baseline
report-only, classify survivors and uncovered code, then set a threshold only after
equivalent mutants and intentional exclusions are understood.

Complete suites and mutation runs belong to configured CI/PitCrew capacity. Local
iteration stays targeted.

## Browser UX evidence

Route-level UX regressions (document overflow, accessibility, heading structure,
focus, and dialog keyboard behavior) are covered by a dedicated Playwright + axe-core
harness against sanitized fixtures, not by component tests. See
[Browser UX evidence harness](testing/browser-ux.md) for the one local command and
what it asserts.

## Guidance-only validation

Documentation, design authority, instructions, ADRs, generated guidance mirrors, and
guidance-contract-only changes use the path-aware `guidance-only` scope. Required CI
and container summary checks remain present while runtime, frontend, installer, and
image jobs are skipped. Missing, mixed, or unknown path evidence falls back to full
validation.
