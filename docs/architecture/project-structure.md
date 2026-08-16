# .NET project and feature structure

Generated .NET solutions use project boundaries to make dependency direction visible
and vertical slices to keep one business capability together.

## Feature slices

Organize feature projects by business capability first. Repositories, services,
requests, responses, handlers, and identifiers for one capability live together
instead of being separated into feature-wide technical folders.

When a mature sub-feature needs a stronger isolation boundary, it can graduate into
its own feature project. Do not create projects speculatively; a project boundary adds
build, package, and composition cost.

Feature projects do not reference sibling feature projects. Cross-feature contracts
live in an SDK/contract boundary so either side can evolve or move processes without a
direct implementation dependency.

## Abstractions and adapters

Introduce an abstraction when a dependency is interchangeable, must be mocked without
booting the provider, carries licensing/availability risk, or brings native/external
runtime baggage.

The owner publishes `<Owner>.Abstractions`; implementations live under
`<Root>.Adapters.<Vendor>`. Owners and adapters both reference the abstraction, while
the application/bootstrap composition root selects the implementation.

An owner referencing its adapter collapses the swap boundary. An adapter referencing
the owner creates the inverse coupling or a cycle.

## Solution and bootstrap ownership

Application feature projects appear in the solution's feature folder and are included
by the bootstrap/composition project. Test projects remain siblings but are not
bootstrapped into production.

Adapter projects live in the solution's adapter folder. Only applications,
bootstrapping aggregators, tests, and benchmarks reference adapters.

## Internal test access

Testable feature projects declare `InternalsVisibleTo` as project
`AssemblyAttribute` items for their test assemblies and Moq's
`DynamicProxyGenAssembly2`. Keeping this in the project file avoids a second
`AssemblyInfo` ownership surface.

## Central package versions

`Directory.Packages.props` owns versions. Project files reference package names without
inline `Version` attributes so one dependency version governs the solution.

## Plugins

Needlr discovers ordinary concrete services automatically. A plugin exists only for a
registration/lifetime/framework primitive Needlr cannot infer or for one deliberate
startup side effect.

Feature-level plugins live at the feature root. A plugin with no manual concern or
startup behavior should not exist.

## Support plane projects

Support-plane v1 intentionally crosses process boundaries. The Dashboard-owned API
slice lives in `PitCrew.Dashboard.Features.Support` with contracts in
`PitCrew.Dashboard.Features.Support.Abstractions`. Shared request/result crypto lives
in `PitCrew.Support.Protocol`. The relay, transport agent, and diagnostics broker are
separate application projects so their deployable trust boundaries remain visible in
the solution instead of being hidden behind one host process.
