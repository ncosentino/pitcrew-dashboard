# Blazor extensibility

Blazor components compose through explicit content/templates first and runtime
reflection only when the component type genuinely is not known at compile time.

## Content and templates

`RenderFragment` parameters project named or default content into a component.
`RenderFragment<T>` exposes an item/context to a caller-provided template.

Required templates/data use `[EditorRequired]` and non-null initialization so missing
composition inputs surface during development.

Generic components use `@typeparam` for compile-time type safety and trimming/AOT
compatibility.

## Runtime component selection

`DynamicComponent` is appropriate for runtime-selected component types, but its
reflection can conflict with trimming. A finite known set should prefer a typed
switch/dictionary or build-time Needlr discovery.

## Cascading state

Use named cascading values for truly cross-cutting state such as theme or layout
context. Cascading dependencies are implicit, so ordinary component data remains an
explicit parameter or injected service.
