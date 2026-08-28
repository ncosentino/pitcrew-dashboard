# Settings navigation and form composition

This document describes the canonical pattern for settings and administrative
routes in the PitCrew Dashboard frontend.

## Route structure

Settings routes live under `/tenants/:tenantId/settings/`. Each route has:

- **One primary Settings destination** at `/tenants/:tenantId/settings`. It
  resolves owners to General and administrators to Enrollment.
- **One human-readable page title** via `routePresentations` in the feature
  manifest. The shell renders it as the single H1 and sets `document.title`.
- **Breadcrumbs** linking back to the parent settings section.
- **Task navigation** using the shared `TaskWorkspace` and `TaskNavigation`
  primitives. Items are filtered by the current user's tenant role using
  `hasMinimumTenantRole`.

## Settings page composition

```tsx
function SettingsPage({ children }: { children: ReactNode }) {
  const { tenantId, tenant } = useCurrentTenant();
  const items = useMemo(() => {
    // Build items filtered by tenant.role
  }, [tenant.role]);
  return (
    <section className="grid gap-5">
      <ReadinessSummary
        title="Administration context"
        description="Current tenant identity and browser-visible authority."
        items={[
          { label: 'Tenant', value: tenant.displayName },
          { label: 'Stable tenant ID', value: tenant.tenantId },
          { label: 'Your authority', value: tenant.role },
          { label: 'Available tasks', value: items.length },
        ]}
      />
      <TaskWorkspace navigationLabel="Tenant settings" navigationItems={items}>
        {children}
      </TaskWorkspace>
    </section>
  );
}
```

Each settings page wraps its content in `SettingsPage` to keep tenant identity,
authority, and task navigation visible before the selected administration task.

## Change-control ledger

Settings tasks use current-state operational rows before editors or mutations:

- `SettingsTask` provides the task heading without another card layer.
- `OperationalList` and `OperationalRow` present identity, role, scope, expiry,
  activity, and lifecycle state before trailing actions.
- Add/create composers follow the current records inside native `details`
  disclosure instead of competing with the primary scan.
- Comparison tables remain appropriate only when cross-record comparison is the
  task; narrow layouts must not push actions outside the viewport.

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

## One-time values

Enrollment codes and diagnostic credential values use the shared settings-local
`OneTimeValue` result:

- focus moves to the newly issued result;
- the raw value has an explicit copy control and clipboard-failure state;
- the operator can explicitly clear the value from the rendered page;
- only non-secret metadata remains after clear, navigation, reload, revoke, or a
  later mutation.

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

The manifest's `activePathPatterns` for the "Settings" primary navigation item
includes General, Access, Enrollment, and Diagnostics. Child settings routes
never contribute separate primary-navigation items, so desktop and mobile
shells expose one stable Settings parent while task navigation owns the
authorized child destinations.
