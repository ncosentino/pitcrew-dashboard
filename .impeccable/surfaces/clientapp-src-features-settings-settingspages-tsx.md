---
version: 1
slug: "clientapp-src-features-settings-settingspages-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/SettingsPages.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/DiagnosticCredentials.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/TenantAdministration.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/TenantSettings.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/manifest.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/admin/TenantCreationPage.tsx"]
---

## Scope and mode

Tenant general settings, access, connector enrollment, diagnostic credentials, and
system-administrator tenant creation. Visitor mode: Operate.

## Audience and job

An authorized administrator changes tenant identity or membership, enrolls a
connector, or creates, rotates, and revokes narrowly scoped diagnostic credentials
without losing track of scope or consequence.

## Hierarchy and interaction

- Secondary navigation has an unmistakable selected state.
- Every field has a persistent visible label; placeholders contain examples only.
- Instructions state format, eligibility, scope, expiry, one-time visibility, and
  recovery limitations before submission.
- Credential rotation and revocation use the shared consequential confirmation
  contract.
- Success confirms the completed outcome and one-time secret handling without
  retaining raw credentials.

## Responsive behavior and states

Forms become one column on narrow screens and keep actions within the viewport.
Membership and credential records recompose or scroll inside labeled regions. Cover
loading, empty membership, no available users, validation failure, permission denial,
busy, successful creation, one-time value, active, rotated, revoked, and expired
credential states.

## Direction and anti-goals

The memorable moment is safe certainty about scope and consequence. Avoid
placeholder-only labels, visually identical settings tabs, confirmation-free
credential changes, secret-shaped examples, and tables that push actions off-screen.
