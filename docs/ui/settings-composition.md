# Settings navigation and form composition

This document describes the canonical pattern for settings and administrative
routes in the PitCrew Dashboard frontend.

## Route structure

Settings routes live under `/tenants/:tenantId/settings/`. Each route has:

- **One human-readable page title** via `routePresentations` in the feature
  manifest. The shell renders it as the single H1 and sets `document.title`.
- **Breadcrumbs** linking back to the parent settings section.
- **Section navigation** using the shared `SectionNavigation` primitive from
  `@/core/ui/SectionNavigation`. Items are filtered by the current user's
  tenant role using `hasMinimumTenantRole`.

## Settings page composition

```tsx
function SettingsPage({ children }: { children: ReactNode }) {
  const { tenantId, tenant } = useCurrentTenant();
  const items = useMemo(() => {
    // Build items filtered by tenant.role
  }, [tenant.role]);
  return (
    <section className="grid gap-4">
      <SectionNavigation label="Tenant settings" items={items} />
      {children}
    </section>
  );
}
```

Each settings page wraps its content in `SettingsPage` to get consistent
secondary navigation with clear active states.

## Form fields

All form inputs use the `FormField` wrapper from `@/core/ui/FormField`:

- Persistent visible labels (never placeholder-only).
- Hints describe format, eligibility, or scope.
- Validation errors are announced to assistive technology.

## Consequential operations

Destructive or irreversible actions (credential rotation, revocation, member
removal) use `ConfirmActionDialog` with `ConfirmationSummary`:

- **Identity**: what is being acted on (label, ID).
- **Effects**: what will happen.
- **Prohibited effects**: what will not happen (scope preservation).
- Optional **acknowledgement** checkbox for high-consequence operations.

## Stable identifiers

IDs are shown as secondary copyable metadata using `CopyableId` from
`@/core/ui/CopyableId`. Human-readable names always lead the hierarchy per
DESIGN.md's "The Human Name First Rule".

## Role-gated visibility

| Route | Minimum role |
|-------|-------------|
| `/settings/general` | owner |
| `/settings/access` | owner |
| `/settings/enrollment` | administrator |
| `/settings/diagnostics` | administrator |

The `TenantRouteGuard` component enforces role requirements at the route
level. The section navigation only renders tabs the current role can access.

## Primary navigation active state

The manifest's `activePathPatterns` for the "Settings" primary navigation
item includes both `/settings/general` and `/settings/access`, ensuring the
primary nav link shows as active across all settings sub-routes.
