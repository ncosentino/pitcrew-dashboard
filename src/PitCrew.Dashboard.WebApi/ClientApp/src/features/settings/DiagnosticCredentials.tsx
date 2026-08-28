import { useCallback, useEffect, useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ApiError } from '@/core/api/httpClient';
import { formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { EmptyState } from '@/core/ui/EmptyState';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  createDiagnosticCredential,
  getDiagnosticCredentials,
  revokeDiagnosticCredential,
  rotateDiagnosticCredential,
  type DiagnosticCredential,
  type DiagnosticCredentialCreated,
} from './settingsApi';
import { SettingsTask } from './SettingsTask';
import { OneTimeValue } from './OneTimeValue';

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
  const [isLoading, setIsLoading] = useState(true);
  const [isBusy, setIsBusy] = useState(false);

  const load = useCallback(
    async (signal: AbortSignal) => {
      try {
        setCredentials(await getDiagnosticCredentials(tenantId, signal));
        setError(null);
      } catch (caught) {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        setError(caught instanceof Error ? caught.message : 'Credentials could not be loaded.');
      } finally {
        if (!signal.aborted) setIsLoading(false);
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
    <SettingsTask
      title="Read-only diagnostic access"
      description="Issue tenant-scoped credentials for headless fleet and history queries. Raw values are shown once."
    >
      <div className="grid gap-4">
        {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
        {issued ? (
          <OneTimeValue
            title="Diagnostic credential ready"
            value={issued.value}
            description={`Credential ${issued.credential.credentialId}. Expires ${formatTime(issued.credential.expiresAt)}. Scope: ${issued.credential.nodeIds.length === 0 ? 'all nodes' : `${issued.credential.nodeIds.length} nodes`}, ${issued.credential.profileIds.length === 0 ? 'all profiles' : `${issued.credential.profileIds.length} profiles`}. Send it only in the Authorization header with the PitCrew-Diagnostics scheme. It is not stored in recoverable form.`}
            onClear={() => setIssued(null)}
          />
        ) : null}
        {isLoading ? <LoadingState label="Loading diagnostic credentials…" /> : null}
        {!isLoading && error === null && credentials.length === 0 ? (
          <EmptyState
            title="No diagnostic credentials"
            description="No tenant-scoped diagnostic credential metadata is currently visible. Create one only when a headless read-only integration needs it."
          />
        ) : null}
        {!isLoading && credentials.length > 0 ? (
          <OperationalList label="Issued diagnostic credentials">
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
          </OperationalList>
        ) : null}
        <details className="rounded-lg border bg-card">
          <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold outline-none hover:bg-muted/40 focus-visible:ring-2 focus-visible:ring-ring">
            <span>Create a credential</span>
            <span className="text-xs font-normal text-muted-foreground">
              {isLoading ? 'Loading' : `${credentials.length} issued`}
            </span>
          </summary>
          <form
            className="grid gap-3 border-t p-4 md:grid-cols-2"
            onSubmit={(event) => {
              event.preventDefault();
              void create();
            }}
          >
            <FormField label="Credential label" hint="A human-readable name for this credential.">
              <input
                className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm"
                value={label}
                maxLength={128}
                onChange={(event) => setLabel(event.target.value)}
              />
            </FormField>
            <FormField label="Expiry (hours)" hint="Between 1 and 8760 hours from creation.">
              <input
                className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm"
                type="number"
                min={1}
                max={8760}
                inputMode="numeric"
                value={expiryHours}
                onChange={(event) => setExpiryHours(event.target.value)}
              />
            </FormField>
            <FormField label="Allowed node IDs" hint="Comma-separated; leave empty for all nodes.">
              <input
                className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm"
                value={nodeIds}
                onChange={(event) => setNodeIds(event.target.value)}
              />
            </FormField>
            <FormField
              label="Allowed profile IDs"
              hint="Comma-separated; leave empty for all profiles."
            >
              <input
                className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm"
                value={profileIds}
                onChange={(event) => setProfileIds(event.target.value)}
              />
            </FormField>
            <Button
              className="justify-self-start md:col-span-2"
              type="submit"
              disabled={isBusy || label.trim().length === 0}
            >
              {isBusy ? 'Creating…' : 'Create diagnostic credential'}
            </Button>
          </form>
        </details>
      </div>
    </SettingsTask>
  );
}

interface CredentialRowProps {
  readonly credential: DiagnosticCredential;
  readonly isBusy: boolean;
  readonly onRotate: () => void;
  readonly onRevoke: () => void;
}

function CredentialRow({ credential, isBusy, onRotate, onRevoke }: CredentialRowProps) {
  const status = getCredentialStatus(credential);
  const actionsDisabled = isBusy || status !== 'active';
  return (
    <OperationalRow
      title={credential.label}
      description={`Expires ${formatTime(credential.expiresAt)}`}
      status={
        <StatusBadge
          status={status}
          tone={status === 'active' ? 'positive' : status === 'revoked' ? 'critical' : 'caution'}
        />
      }
      metadata={
        <div className="grid min-w-0 gap-3 text-xs text-muted-foreground sm:grid-cols-2">
          <div className="min-w-0">
            <div className="font-medium text-foreground">Scope</div>
            <div className="[overflow-wrap:anywhere]">
              Nodes: {credential.nodeIds.length === 0 ? 'all' : credential.nodeIds.join(', ')}
            </div>
            <div className="[overflow-wrap:anywhere]">
              Profiles:{' '}
              {credential.profileIds.length === 0 ? 'all' : credential.profileIds.join(', ')}
            </div>
          </div>
          <div>
            <div className="font-medium text-foreground">Activity</div>
            <div>Created {formatTime(credential.createdAt)}</div>
            <div className="[overflow-wrap:anywhere]">
              Created by GitHub user {credential.createdByGitHubUserId}
            </div>
            <div>Uses: {credential.useCount}</div>
            <div>
              Last used: {credential.lastUsedAt ? formatTime(credential.lastUsedAt) : 'never'}
            </div>
            {credential.revokedAt ? (
              <div>
                Revoked {formatTime(credential.revokedAt)}
                {credential.revokedByGitHubUserId
                  ? ` by GitHub user ${credential.revokedByGitHubUserId}`
                  : ''}
              </div>
            ) : null}
          </div>
        </div>
      }
      actions={
        <div className="flex flex-wrap gap-2">
          <ConfirmActionDialog
            trigger={
              <Button type="button" variant="outline" size="sm" disabled={actionsDisabled}>
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
              <Button type="button" variant="outline" size="sm" disabled={actionsDisabled}>
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
      }
    >
      <div className="font-mono text-xs text-muted-foreground [overflow-wrap:anywhere]">
        {credential.credentialId}
      </div>
      {credential.rotatedFromCredentialId ? (
        <div className="mt-1 text-xs text-muted-foreground [overflow-wrap:anywhere]">
          Rotated from {credential.rotatedFromCredentialId}
        </div>
      ) : null}
    </OperationalRow>
  );
}

function getCredentialStatus(credential: DiagnosticCredential): 'active' | 'expired' | 'revoked' {
  if (credential.revokedAt) return 'revoked';
  return Date.parse(credential.expiresAt) <= Date.now() ? 'expired' : 'active';
}
