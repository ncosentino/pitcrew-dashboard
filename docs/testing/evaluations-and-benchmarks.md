# Evaluations and benchmarks

Evaluations and benchmarks both produce evidence. Their harness must isolate the thing
being measured, preserve comparable conditions, and report enough context to explain a
result.

## Evaluation suites

Define success per independent capability, safety, quality, latency, or cost dimension.
Each metric records its unit, population, aggregation, target/baseline, and whether it
is a gate or diagnostic.

Cases represent important production distributions and named risk slices. Keep
development/regression cases separate from held-out validation; once a held-out case
is inspected for tuning, it is no longer held out.

Distinguish:

- a case: one input and success contract;
- a trial: one independent execution;
- an attempt: an infrastructure retry within a trial.

Retries repair infrastructure failures; they do not erase subject failures.

Deterministic contracts use deterministic graders. Model judges are reserved for
semantic dimensions, return evidence plus explicit inconclusive outcomes, and are
calibrated against human-labelled clear, borderline, adversarial, missing-evidence,
and malformed-output cases.

Record every trial before policy decides pass/fail/inconclusive. Preserve subject,
model, instructions, tools, fixture, grader/rubric version, usage, duration, artifacts,
environment, and source revision.

## Benchmark harnesses

Benchmark methods contain only the production call under test. Setup, data generation,
validation, logging, and assertions live outside timed code.

Comparisons run baseline and candidate in the same class/run with the same input and
consumption pattern. A strategy switch inside one benchmark measures the branch as
well as the strategy and is not a valid comparison.

Benchmarks call real production code through a project reference. Correctness tests in
the normal test project prove equivalent outcomes.

Use memory diagnostics, representative parameter ranges, and a GC/runtime mode that
matches the host application. Avoid real I/O unless I/O itself is the subject.

Retire comparative benchmarks when the losing implementation is removed, unless both
strategies remain product capabilities.
