import { useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { ApiError } from '@/core/api/httpClient';

import { renameTenant } from './accessApi';

/** Props for owner-managed tenant settings. */
export interface TenantSettingsProps {
  readonly tenantId: string;
  readonly displayName: string;
  readonly antiforgeryToken: string;
  readonly onRenamed: (displayName: string) => void;
}

/** Allows a tenant owner to change the operator-facing name while preserving its stable ID. */
export function TenantSettings({
  tenantId,
  displayName,
  antiforgeryToken,
  onRenamed,
}: TenantSettingsProps) {
  const [nextDisplayName, setNextDisplayName] = useState(displayName);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);

  const normalizedDisplayName = nextDisplayName.trim();
  const canSave =
    normalizedDisplayName.length > 0 &&
    normalizedDisplayName.length <= 128 &&
    normalizedDisplayName !== displayName;

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    setIsBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await renameTenant(tenantId, normalizedDisplayName, antiforgeryToken);
      setNextDisplayName(normalizedDisplayName);
      onRenamed(normalizedDisplayName);
      setSuccess('Tenant name updated.');
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? caught.message
          : caught instanceof Error
            ? caught.message
            : 'Tenant name could not be changed.',
      );
    } finally {
      setIsBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tenant settings</CardTitle>
        <CardDescription>
          Change the operator-facing name. The stable tenant ID remains {tenantId}.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="grid gap-3 sm:grid-cols-[1fr_auto]"
          onSubmit={(event) => void submit(event)}
        >
          <label className="grid gap-1 text-sm font-medium">
            Tenant display name
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              maxLength={128}
              value={nextDisplayName}
              onChange={(event) => setNextDisplayName(event.target.value)}
            />
          </label>
          <Button className="self-end" type="submit" disabled={isBusy || !canSave}>
            {isBusy ? 'Saving…' : 'Rename tenant'}
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
      </CardContent>
    </Card>
  );
}
