---
version: 1
slug: "clientapp-src-features-settings-settingspages-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/SettingsPages.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/TenantSettings.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/TenantAdministration.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/DiagnosticCredentials.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/OneTimeValue.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/SettingsTask.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/manifest.tsx"]
---

## Scope and mode

Tenant general settings, access, connector enrollment, and diagnostic credentials.
Visitor mode: Operate.

## Audience and job

An authorized tenant administrator changes identity or membership, enrolls a
connector, or creates, rotates, and revokes narrowly scoped diagnostic credentials
without losing track of the tenant, current authority, existing state, or
consequence.

## Hierarchy and interaction

- Keep one persistent administration context above every task with the tenant display
  name, immutable tenant ID, current tenant role, and available task count.
- Preserve General, Access, Enrollment, and Diagnostics as role-filtered,
  deep-linkable tasks inside the shared task workspace.
- Present current identity, membership, credential scope, expiry, activity, and
  lifecycle as full-width operational rows before any editor or mutation.
- Put add/create composers after current records inside native disclosure. Keep
  remove, rotate, and revoke actions on the record they affect with the shared
  consequence confirmation.
- Focus every newly issued enrollment or diagnostic value, provide explicit copy and
  clipboard-failure feedback, and require confirmation before clearing the
  unrecoverable browser value.
- Browser visibility reflects tenant role only; server APIs remain final authority.

## Responsive behavior and states

At narrow widths, task navigation remains a contained horizontal strip, records stay
full width, metadata wraps inside its row, and add/create disclosure becomes one
column. Cover loading, empty membership, no available users, permission denial,
validation failure, mutation failure, busy, successful creation, one-time value,
copy failure, active, expired, rotated, revoked, and long-content states across light
and dark themes. Raw credential material never reappears after clear, navigation, or
reload.

## Direction and anti-goals

Direction: change-control ledger, grounded surface candidate 6, seed `2f14eb4c`.
The memorable moment is safe handoff: existing authority and state remain visible,
then a newly issued one-time value takes focus until the operator copies or
deliberately clears it. Avoid card-heavy form walls, action-first rows, visually
identical tasks, confirmation-free lifecycle changes, hidden role ownership,
secret-shaped examples, and tables that push actions outside the viewport.
