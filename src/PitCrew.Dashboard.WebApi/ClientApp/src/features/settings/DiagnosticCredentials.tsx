import { useCallback, useEffect, useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { ApiError } from '@/core/api/httpClient';
import { formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { FormField } from '@/core/ui/FormField';

import {
  createDiagnosticCredential,
  getDiagnosticCredentials,
  revokeDiagnosticCredential,
  rotateDiagnosticCredential,
  type DiagnosticCredential,
  type DiagnosticCredentialCreated,
} from './settingsApi';

/** Props for administrator-managed diagnostic credentials. */
export interface DiagnosticCredentialsProps {
  readonly tenantId: string;
  readonly antiforgeryToken: string;
}

/** Creates, rotates, revokes, and audits scoped read-only diagnostic credentials. */
export function DiagnosticCredentials({ tenantId, antiforgeryToken }: DiagnosticCredentialsProps) {
  const [credentials, setCredentials] = useState<readonly DiagnosticCredential[]>([]);
  const [label, setLabel] = useState('Performance diagnostics');
  const [expiryHours, setExpiryHours] = useState('24');
  const [nodeIds, setNodeIds] = useState('');
  const [profileIds, setProfileIds] = useState('');
  const [issued, setIssued] = useState<DiagnosticCredentialCreated | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);

  const load = useCallback(
    async (signal: AbortSignal) => {
      try {
        setCredentials(await getDiagnosticCredentials(tenantId, signal));
        setError(null);
      } catch (caught) {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        setError(caught instanceof Error ? caught.message : 'Credentials could not be loaded.');
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void load(controller.signal);
    }, 0);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [load]);

  const mutate = async (operation: () => Promise<DiagnosticCredentialCreated | null>) => {
    setIsBusy(true);
    try {
      const nextIssued = await operation();
      setIssued(nextIssued);
      await load(new AbortController().signal);
      setError(null);
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? caught.message
          : caught instanceof Error
            ? caught.message
            : 'Diagnostic credential could not be changed.',
      );
    } finally {
      setIsBusy(false);
    }
  };

  const parseList = (value: string) =>
    value
      .split(',')
      .map((item) => item.trim())
      .filter((item) => item.length > 0);

  const create = async () => {
    const hours = Number.parseInt(expiryHours, 10);
    if (!Number.isInteger(hours) || hours < 1 || hours > 8760) {
      setError('Expiry must be between 1 and 8760 hours.');
      return;
    }
    await mutate(
      async () =>
        await createDiagnosticCredential(
          tenantId,
          label.trim(),
          new Date(Date.now() + hours * 60 * 60 * 1000).toISOString(),
          parseList(nodeIds),
          parseList(profileIds),
          antiforgeryToken,
        ),
    );
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle as="h2">Diagnostic credentials</CardTitle>
        <CardDescription>
          Issue tenant-scoped read-only credentials for headless fleet and history queries. Raw
          values are shown once.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
        <fieldset className="grid gap-3 rounded-lg border p-4">
          <legend className="px-2 text-sm font-medium">Create a new credential</legend>
          <FormField label="Credential label" hint="A human-readable name for this credential.">
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={label}
              maxLength={128}
              onChange={(event) => setLabel(event.target.value)}
            />
          </FormField>
          <FormField label="Expiry (hours)" hint="Between 1 and 8760 hours from creation.">
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              inputMode="numeric"
              value={expiryHours}
              onChange={(event) => setExpiryHours(event.target.value)}
            />
          </FormField>
          <FormField label="Allowed node IDs" hint="Comma-separated; leave empty for all nodes.">
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={nodeIds}
              onChange={(event) => setNodeIds(event.target.value)}
            />
          </FormField>
          <FormField
            label="Allowed profile IDs"
            hint="Comma-separated; leave empty for all profiles."
          >
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={profileIds}
              onChange={(event) => setProfileIds(event.target.value)}
            />
          </FormField>
          <Button
            type="button"
            disabled={isBusy || label.trim().length === 0}
            onClick={() => void create()}
          >
            Create diagnostic credential
          </Button>
        </fieldset>
        {issued ? (
          <div
            className="grid gap-2 rounded-lg border border-status-caution-foreground/30 bg-status-caution p-4 text-status-caution-foreground"
            role="status"
          >
            <div className="text-sm font-semibold">Copy this credential now</div>
            <code className="overflow-x-auto rounded bg-background p-3 text-xs text-foreground">
              {issued.value}
            </code>
            <div className="text-xs">
              Send it only in the Authorization header using the PitCrew-Diagnostics scheme. It is
              not stored in recoverable form.
            </div>
          </div>
        ) : null}
        <div className="min-w-0 overflow-x-auto">
          <table className="w-full min-w-3xl text-left text-sm">
            <caption className="px-2 py-2 text-left font-semibold">
              Issued diagnostic credentials
            </caption>
            <thead className="text-xs text-muted-foreground uppercase">
              <tr>
                <th scope="col" className="px-2 py-2">
                  Credential
                </th>
                <th scope="col" className="px-2 py-2">
                  Scope
                </th>
                <th scope="col" className="px-2 py-2">
                  Activity
                </th>
                <th scope="col" className="px-2 py-2 text-right">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {credentials.map((credential) => (
                <CredentialRow
                  key={credential.credentialId}
                  credential={credential}
                  isBusy={isBusy}
                  onRotate={() =>
                    void mutate(
                      async () =>
                        await rotateDiagnosticCredential(
                          tenantId,
                          credential.credentialId,
                          antiforgeryToken,
                        ),
                    )
                  }
                  onRevoke={() =>
                    void mutate(async () => {
                      await revokeDiagnosticCredential(
                        tenantId,
                        credential.credentialId,
                        antiforgeryToken,
                      );
                      return null;
                    })
                  }
                />
              ))}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}

interface CredentialRowProps {
  readonly credential: DiagnosticCredential;
  readonly isBusy: boolean;
  readonly onRotate: () => void;
  readonly onRevoke: () => void;
}

function CredentialRow({ credential, isBusy, onRotate, onRevoke }: CredentialRowProps) {
  return (
    <tr className="border-t align-top">
      <td className="px-2 py-2">
        <div className="font-medium">{credential.label}</div>
        <div className="font-mono text-xs text-muted-foreground">{credential.credentialId}</div>
        <div className="text-xs text-muted-foreground">
          Expires {formatTime(credential.expiresAt)}
        </div>
      </td>
      <td className="px-2 py-2 text-xs">
        <div>Nodes: {credential.nodeIds.length === 0 ? 'all' : credential.nodeIds.join(', ')}</div>
        <div>
          Profiles: {credential.profileIds.length === 0 ? 'all' : credential.profileIds.join(', ')}
        </div>
      </td>
      <td className="px-2 py-2 text-xs">
        <div>{credential.revokedAt ? `Revoked ${formatTime(credential.revokedAt)}` : 'Active'}</div>
        <div>Last used: {credential.lastUsedAt ? formatTime(credential.lastUsedAt) : 'never'}</div>
        <div>Uses: {credential.useCount}</div>
      </td>
      <td className="px-2 py-2 text-right">
        <div className="flex justify-end gap-2">
          <ConfirmActionDialog
            trigger={
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isBusy || credential.revokedAt !== null}
              >
                Rotate
              </Button>
            }
            title={`Rotate "${credential.label}"?`}
            description="Rotating replaces the active credential value. Existing integrations using this credential will stop authenticating until they receive the new value."
            confirmLabel="Rotate credential"
            details={
              <ConfirmationSummary
                identity={[
                  { label: 'Credential', value: credential.label },
                  { label: 'ID', value: credential.credentialId },
                ]}
                effects={[
                  'A new credential value will be issued.',
                  'The previous value becomes invalid immediately.',
                ]}
                prohibitedEffects={['Scope and expiry settings remain unchanged.']}
              />
            }
            onConfirm={onRotate}
          />
          <ConfirmActionDialog
            trigger={
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isBusy || credential.revokedAt !== null}
              >
                Revoke
              </Button>
            }
            title={`Revoke "${credential.label}"?`}
            description="Revoking permanently disables this credential. All integrations using it will stop authenticating."
            confirmLabel="Revoke credential"
            confirmVariant="destructive"
            details={
              <ConfirmationSummary
                identity={[
                  { label: 'Credential', value: credential.label },
                  { label: 'ID', value: credential.credentialId },
                  {
                    label: 'Uses',
                    value: `${credential.useCount} total`,
                  },
                ]}
                effects={[
                  'This credential is permanently revoked.',
                  'It cannot be rotated or reinstated.',
                ]}
              />
            }
            onConfirm={onRevoke}
          />
        </div>
      </td>
    </tr>
  );
}
