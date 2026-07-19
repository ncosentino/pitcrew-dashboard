---
# AUTO-GENERATED from .github/instructions/genesis/project-structure.instructions.md — do not edit
paths:
  - "**/*.csproj"
---
# Project File Rules

## Feature Projects in Bootstrap

- Feature projects that ship as part of the application must be referenced in the Bootstrap project.
- Feature test projects do NOT get included in bootstrap

## SLNX

- You must include the feature project in the SLNX
- The feature project folder exists at the repo root, but within the solution file's "Feature" section
- The feature's test project folder is a sibling, and the reference in the SLNX is also a sibling

## Cross Feature References

- It is STRICTLY prohibited to reference another feature project across feature boundaries. You must route such concerns via the SDK project.
- A project that is a specific implementation of a feature can reference the feature project that is the namespace level above it. So MyProduct.Features.TheFeatureArea.TheFeatureImplementation can reference MyProduct.Features.TheFeatureArea.

## Abstractions & Adapters

When a project takes a third-party dependency, consider splitting that dependency behind an interface so the rest of the solution stays decoupled from it. This is a project-shape decision; it belongs at csproj-edit time.

### When to split

Split when **any** of these is true:

1. The third-party is interchangeable — alternative vendors exist or are plausible (e.g. cache provider, AI model host, storage backend).
2. You want to test consumers of the contract without booting the third-party.
3. The third-party has licensing or availability risk that warrants a swap-out lever.
4. The third-party brings unmanaged or external dependencies along — native DLLs, SQLite drivers, NetVips native binaries, headless-browser binaries, etc. Isolating that baggage to an adapter keeps the rest of the solution simple to build and ship.

If **none** of these apply, take the third-party dep directly without ceremony. Do not spin up an empty Abstractions project just for purity.

### Naming

- **Abstractions project**: `<Owner>.Abstractions`, sibling of the owner that publishes the contract.
- **Adapter project**: `<RootNamespace>.Adapters.<Vendor>`. **Plural** `Adapters`. **No** `<Capability>` slot between `Adapters` and `<Vendor>`.

Examples:

```
✅ MyProduct.Kernel.Caching.Abstractions   ← contract, sibling of MyProduct.Kernel.Caching
✅ MyProduct.Adapters.FusionCache          ← implementation, top-level under src/Adapters/

✅ MyProduct.Features.Media.Abstractions   ← contract, sibling of MyProduct.Features.Media
✅ MyProduct.Adapters.NetVips              ← implementation, top-level under src/Adapters/

❌ MyProduct.Kernel.Caching.FusionCache    ← missing "Adapters" marker
❌ MyProduct.Adapters.Caching.FusionCache  ← no <Capability> slot allowed
```

### Where things live in the solution

- `*.Abstractions` projects live as a sibling of their owner in the SLNX, under the same folder as the owner.
- `*.Adapters.<Vendor>` projects live at the top level of the SLNX under `src/Adapters/`, parallel to `src/Features/`, `src/Kernel/`, and `src/Applications/`.

### Who can reference what

In production graphs, the only projects allowed to `ProjectReference` an adapter are **composition / aggregator projects** — projects whose entire reason to exist is to wire up the set of dependencies for a host. The two common shapes are:

1. The **App / host project** itself (e.g. a WebApi, Worker, Functions, or Desktop app project) — it is the literal composition root.
2. A **dependency-bootstrapping aggregator project** that the App references in turn — for example a `*.Bootstrap` project that wildcard-references `Features.*`, `Kernel.*`, and `Adapters.*` to keep the App's `.csproj` thin.

Either pattern is acceptable. The aggregator is just a packaging convenience; conceptually it is still part of the composition root.

Test and benchmark projects may also reference adapters as needed.

Beyond those, the rules are strict:

- **Owner projects** (Features, Kernel, etc.) MUST NOT reference adapters. Owners reference their own `*.Abstractions`; the composition root (directly or via the aggregator) picks the implementation.
- **Adapter projects** MUST NOT reference owner projects. Adapter ➜ Abstractions ➜ (nothing). Owner ➜ Abstractions ➜ (nothing). The composition root pulls both together.

The inverse — an owner referencing an adapter, or an adapter referencing an owner — is forbidden because it collapses the inversion-of-control seam this split exists to create. The whole point of routing owners and adapters through a shared `*.Abstractions` is that the composition root (and only the composition root) decides which implementation gets wired in. The moment an owner takes a direct dependency on an adapter, every consumer of the owner has transitively chosen that vendor, and the swap-out lever is gone. The moment an adapter takes a direct dependency on an owner, you have a reference cycle through the abstraction and the build either breaks or hides a vendor leak.

### `Missing*` fallback

When multiple adapters could legitimately satisfy a contract and a misconfigured graph would otherwise fail with an opaque DI miss, register a `Missing*` implementation from the **owner** project (not from the abstraction, not from any adapter). The `Missing*` implementation throws `InvalidOperationException` with a concrete setup-error message naming the adapter to `ProjectReference` and how tests should mock the interface instead.

## `InternalsVisibleTo` for testable projects

Every feature project that has (or may have) tests MUST declare `InternalsVisibleTo` using the `<AssemblyAttribute>` pattern directly in the `.csproj` file. Do NOT use `AssemblyInfo.cs`.

Add the following `ItemGroup` to every feature project's `.csproj`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>$(AssemblyName).Tests</_Parameter1>
  </AssemblyAttribute>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>$(AssemblyName).Tests.Unit</_Parameter1>
  </AssemblyAttribute>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>$(AssemblyName).Tests.Functional</_Parameter1>
  </AssemblyAttribute>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>DynamicProxyGenAssembly2, PublicKey=0024000004800000940000000602000000240000525341310004000001000100c547cac37abd99c8db225ef2f6c8a3602f3b3606cc9891605d02baa56104f4cfc0734aa39b93bf7852f7d9266654753cc297e7d2edfe0bac1cdcf9f717241550e0a7b191195b7667bb4f64bcb8e2121380fd1d9d46ad2d92d2d15605093924cceaf74c4861eff62abf69b9291ed0a340e113be11e6a7d3113e92484cf7045cc7</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

The first three entries grant the local project's own `.Tests`, `.Tests.Unit`, and `.Tests.Functional` assemblies internal access via `$(AssemblyName)`. The `DynamicProxyGenAssembly2` entry is required for Moq to proxy `internal` types — without it, `MockBehavior.Strict` mocks of internal interfaces will fail at runtime. If your solution has a cross-cutting integration test project that needs visibility into multiple features, add an additional `<AssemblyAttribute>` entry with that project's literal assembly name.

### Why this pattern

Using `<AssemblyAttribute>` in `.csproj` instead of `AssemblyInfo.cs` keeps the project file as the single source of truth and avoids conflicts with implicit using generation. All existing feature projects in this repository follow this pattern.

## Central package management

`Directory.Packages.props` declares all package versions with `ManagePackageVersionsCentrally=true`. Individual `.csproj` files reference packages by name only — never specify a version inline:

```xml
<!-- Directory.Packages.props — single source of truth for versions -->
<PackageVersion Include="Serilog" Version="4.2.0" />

<!-- Feature.csproj — name only, no version -->
<PackageReference Include="Serilog" />
```

When adding a new package:

1. Add the `<PackageVersion>` entry to `Directory.Packages.props` first
2. Then add the `<PackageReference>` (name only) to the consuming `.csproj`

Never use `Version=` on a `<PackageReference>` — it will conflict with central management and produce build warnings or errors.
