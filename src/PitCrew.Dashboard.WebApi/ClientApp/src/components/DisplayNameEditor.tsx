import { useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';

const maximumDisplayNameLength = 128;

/** Props for the shared operator-facing display-name editor. */
export interface DisplayNameEditorProps {
  readonly value: string;
  readonly label: string;
  readonly submitLabel: string;
  readonly successMessage: string;
  readonly onSave: (displayName: string) => Promise<void>;
}

/** Edits one normalized operator-facing display name with inline mutation feedback. */
export function DisplayNameEditor({
  value,
  label,
  submitLabel,
  successMessage,
  onSave,
}: DisplayNameEditorProps) {
  const [draft, setDraft] = useState({
    sourceValue: value,
    persistedValue: value,
    inputValue: value,
  });
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const sourceMatches = draft.sourceValue === value;
  const draftValue = sourceMatches ? draft.inputValue : value;
  const persistedValue = sourceMatches ? draft.persistedValue : value;
  const normalizedValue = draftValue.trim();
  const canSave =
    normalizedValue.length > 0 &&
    normalizedValue.length <= maximumDisplayNameLength &&
    normalizedValue !== persistedValue;

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    setIsBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await onSave(normalizedValue);
      setDraft({
        sourceValue: value,
        persistedValue: normalizedValue,
        inputValue: normalizedValue,
      });
      setSuccess(successMessage);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Display name could not be changed.');
    } finally {
      setIsBusy(false);
    }
  };

  return (
    <form className="grid gap-3 sm:grid-cols-[1fr_auto]" onSubmit={(event) => void submit(event)}>
      <label className="grid gap-1 text-sm font-medium">
        {label}
        <input
          className="h-9 rounded-md border bg-background px-3 text-sm"
          maxLength={maximumDisplayNameLength}
          value={draftValue}
          onChange={(event) =>
            setDraft({
              sourceValue: value,
              persistedValue: value,
              inputValue: event.target.value,
            })
          }
        />
      </label>
      <Button className="self-end" type="submit" disabled={isBusy || !canSave}>
        {isBusy ? 'Saving…' : submitLabel}
      </Button>
      {error ? (
        <p className="text-sm text-red-700 sm:col-span-2 dark:text-red-300" role="alert">
          {error}
        </p>
      ) : null}
      {success ? (
        <p className="text-sm text-teal-800 sm:col-span-2 dark:text-teal-200" role="status">
          {success}
        </p>
      ) : null}
    </form>
  );
}
