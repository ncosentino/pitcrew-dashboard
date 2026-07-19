---
# AUTO-GENERATED from .github/instructions/genesis/benchmarks.instructions.md — do not edit
paths:
  - "**/*Benchmark*/**/*.cs"
  - "**/*Benchmark*.cs"
---
# BenchmarkDotNet Benchmarks

Trustworthy numbers only — every rule below exists to prevent a false measurement or a fake comparison.

## Isolate exactly what you measure

A `[Benchmark]` method MUST contain only the call(s) under test — nothing else:

- No inline data/random generation, fixtures, validation, assertions, branching, or logging.
- Move all of that to `[GlobalSetup]` (or `[IterationSetup]`, only when mutation requires fresh state per
  iteration) or a field.
- The only exception is whatever stops JIT dead-code elimination — return the value, or feed a
  `BenchmarkDotNet.Engines.Consumer`.

```csharp
// ❌ WRONG — generates input, validates, and logs inside the timed method
[Benchmark]
public int Sum()
{
    var data = Enumerable.Range(0, 10_000).ToArray();
    var result = data.Sum();
    if (result != 49_995_000)
    {
        throw new InvalidOperationException("Unexpected result.");
    }

    Console.WriteLine(result);
    return result;
}

// ✅ CORRECT — data built once in GlobalSetup; the benchmark method is just the call under test
private int[] _data = [];

[GlobalSetup]
public void Setup()
{
    _data = Enumerable.Range(0, 10_000).ToArray();
}

[Benchmark]
public int Sum() => _data.Sum();
```

## Never reimplement the production code under test

A benchmark MUST call the real production type/method via an actual project reference — never a
hand-written stand-in in the benchmark project. A rewritten "what it probably does" is an unfalsifiable
number that drifts silently from reality.

- Reference the owning project directly (`ProjectReference`), same as a `.Tests` project.
- Grant `InternalsVisibleTo` for `internal` members — don't duplicate logic to reach them.
- Benchmarking a retired implementation? Restore the real old code (e.g. from git history) — don't recreate
  it from memory.

## Comparing implementations: real baseline, same harness

A performance comparison MUST use two `[Benchmark]` methods, one marked `[Benchmark(Baseline = true)]` —
never a single number reasoned about from memory. Skip this only when there's no comparison goal (e.g.
tracking a steady-state number).

Both methods MUST share a harness:

- Same class, same execution run — never compare numbers across separate runs (machine load, thermal
  state, and runtime version drift invalidate it).
- Same input built once in `[GlobalSetup]` — if shapes differ (`List<T>` vs `Span<T>`), derive both from
  the same source and use `[GlobalSetup(Target = nameof(Method))]` for shape-specific setup.
- Same consumption pattern on both sides (both return, or both feed a `Consumer`) — an asymmetric pattern
  lets the JIT dead-code-eliminate only one side.

NEVER put a `[Params]` strategy enum + `if`/`switch` inside one `[Benchmark]` method — the branch itself
pollutes the measurement and defeats baseline ratio reporting. Always use two methods.

```csharp
// ❌ WRONG — one method, strategy switch inside the timed body
public enum Strategy { Current, Optimized }

[Params(Strategy.Current, Strategy.Optimized)]
public Strategy Strategy { get; set; }

[Benchmark]
public int Sum() => Strategy switch
{
    Strategy.Current => SumService.SumCurrent(_data),
    Strategy.Optimized => SumService.SumOptimized(_data),
    _ => throw new ArgumentOutOfRangeException(nameof(Strategy)),
};

// ✅ CORRECT — two methods, one baseline, same shared input
[Benchmark(Baseline = true)]
public int Current() => SumService.SumCurrent(_data);

[Benchmark]
public int Optimized() => SumService.SumOptimized(_data);
```

A faster implementation that returns a different result is a different, faster bug, not a valid
comparison. Back every comparison with a correctness test in the project's `.Tests` suite (never inline in
the benchmark class) proving equivalent output across representative inputs.

## Naming and lifecycle: promotion retires the comparison

- Name the pair consistently (`Current`/`Optimized`, or the actual strategy names) so the report reads
  without opening source.
- Promoting a winner renames it to the canonical name — drop the "optimized" qualifier. Qualifiers must not
  accumulate across rounds.
- Retire the comparative benchmark in the same change that retires the losing implementation — a
  comparison with only one side left just blocks retiring the old code.
- Exception: implementations meant to coexist permanently for a real functional reason (e.g.
  runtime-selectable strategies) keep their comparison benchmark.

## General hygiene

- `[MemoryDiagnoser]` by default — allocations matter as much as wall-clock time.
- Use `[Params]`/`[Arguments]`/`[ArgumentsSource]` for realistic input sizes — one data point doesn't show
  scaling.
- NEVER hit real network/disk/DB inside a benchmark unless that I/O is the subject.
- Benchmark GC mode MUST match the host application's to be representative.
- BenchmarkDotNet enforces Release-only by default — don't override it with `ManualConfig`.
- Note a micro-benchmark's call volume/hot-path context so reviewers can judge whether it matters.
