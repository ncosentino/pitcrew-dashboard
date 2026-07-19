---
# AUTO-GENERATED from .github/instructions/genesis/react-spa/component-props.instructions.md — do not edit
paths:
  - "**/*.tsx"
---
# React Component Props Rules

These rules apply to every React component file (`.tsx`). They cover the
contract surface that other code reads and that tests assert against.

## Readonly props

Every field on a `Props`/`*Props` interface MUST be declared `readonly`.
Prefer `ReadonlyArray<T>` over `T[]` for array fields. Mutability inside
the component body is fine; the prop contract is not.

```tsx
// ❌ WRONG
interface ButtonProps {
  label: string;
  items: string[];
  onClick: () => void;
}

// ✅ CORRECT
interface ButtonProps {
  readonly label: string;
  readonly items: ReadonlyArray<string>;
  readonly onClick: () => void;
}
```

## No test-only props on production components

Production component prop interfaces MUST NOT contain test plumbing —
`now`, `forceLoading`, `testFetchImpl`, `__seamForTests`, and similar
escape hatches that only exist so a test can drive the component into
a state. Inject those seams via context providers, swappable factories,
or DI — not via prop holes.

The test for whether a prop counts as "production": would a real caller
ever pass it? If the only caller is a test, it doesn't belong on the
interface.

```tsx
// ❌ WRONG — test plumbing leaking into production interface
interface DashboardPageProps {
  readonly addDaemonPath?: string;
  readonly now?: Date;            // test-only
  readonly forceLoading?: boolean; // test-only
  readonly testFetchImpl?: typeof fetch; // test-only
}

// ✅ CORRECT — production interface only; tests inject seams via context
interface DashboardPageProps {
  readonly addDaemonPath?: string;
}
```

## Stable, locale-independent `data-testid` values

Elements whose user-visible text comes from i18n (`useTranslation`) MUST
use `data-testid` values that do not depend on the rendered text. Tests
that look up elements by their translated copy break the moment a locale
changes. Encode role and identity instead.

```tsx
// ❌ WRONG — relies on English text staying constant
<button>{t('row.actions.edit')}</button>

screen.getByText('Edit'); // breaks under es-ES, pseudo-locale, etc.

// ✅ CORRECT — testid encodes role + identity
<button data-testid={`daemons-row-edit-${daemon.id}`}>
  {t('row.actions.edit')}
</button>

screen.getByTestId(`daemons-row-edit-${daemon.id}`);
```

Recommended testid shape: `<feature-or-component-id>-<role>[-<dynamic-id>]`.
