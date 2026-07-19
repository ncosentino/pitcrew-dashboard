---
applyTo: "**/*.csproj,**/*.cs,**/*.slnx"
---

# .NET Build & Test Commands

## Build

```sh
dotnet build
```

## Test

Tests run under **Microsoft.Testing.Platform (MTP)** — `global.json` pins
`"test": { "runner": "Microsoft.Testing.Platform" }`. The run command depends on the framework.

### If using TUnit

```sh
dotnet test
```

- A positional `dotnet test path/to/sln.slnx` is rejected on .NET 10 — run from the solution
  directory, or pass `--solution` / `--project`.
- An empty test project exits code **8** ("zero tests ran"), treated as a failure — ship at least
  one passing test with any new `*.Tests` project.

### If using xUnit

`dotnet test` does not work while the MTP runner is pinned: a VSTest-based xUnit project is refused
(`All projects must use that test runner`), and an MTP xUnit v3 project does not run cleanly through
the `dotnet test` orchestrator. Run the test project as an executable instead:

```sh
dotnet run --project path/to/PitCrew.Dashboard.Tests
```

Do not remove the `global.json` runner pin to force `dotnet test` to accept a VSTest xUnit project.

Always record exact pass/fail/skip and warning/error counts; never report "all tests pass" without
the numbers.
