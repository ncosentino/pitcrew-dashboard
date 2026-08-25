import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { FormField } from '@/core/ui/FormField';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  createSupportEnrollment,
  createSupportSession,
  getSupportIdentities,
  getSupportSession,
  getSupportSessions,
  revokeSupportIdentity,
  type CreatedSupportEnrollment,
  type SupportIdentity,
  type SupportSession,
} from './supportApi';

const diagnosticModes = [
  'ConnectorOffline',
  'CapacityMismatch',
  'JobNotAssigned',
  'HostPressure',
  'Full',
] as const;
const sessionRefreshIntervalMilliseconds = 5_000;
const maximumAutomaticallyRefreshedSessions = 16;

const rejectionGuidance: Partial<
  Record<NonNullable<SupportSession['rejectionDisposition']>, string>
> = {
  'broker-invalid-mode': 'The local broker rejected the requested diagnostic mode.',
  'broker-invalid-profile': 'The profile ID is not configured on the node.',
  'broker-script-missing': 'The approved PitCrew diagnostics collector is missing.',
  'broker-evidence-access-denied': 'The broker cannot read the approved evidence set.',
  'broker-execution-failed': 'The fixed diagnostics collector failed during execution.',
  'broker-response-invalid': 'The broker returned an unsupported bounded status.',
  'broker-io-unavailable': 'The agent could not communicate with the local broker.',
  'broker-timeout': 'The local broker exceeded its bounded execution time.',
};

/** Tenant support-plane diagnostics workflow. */
export default function SupportPage() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const [identities, setIdentities] = useState<readonly SupportIdentity[]>([]);
  const [sessions, setSessions] = useState<readonly SupportSession[]>([]);
  const [nodeId, setNodeId] = useState('');
  const [mode, setMode] = useState<(typeof diagnosticModes)[number]>('ConnectorOffline');
  const [profileId, setProfileId] = useState('');
  const [enrollment, setEnrollment] = useState<CreatedSupportEnrollment | null>(null);
  const [displayName, setDisplayName] = useState('Support node');
  const [error, setError] = useState<string | null>(null);
  const [sessionRefreshError, setSessionRefreshError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [revokingNodeId, setRevokingNodeId] = useState<string | null>(null);
  const sessionRefreshController = useRef<AbortController | null>(null);
  const activeIdentities = identities.filter((identity) => identity.status === 'Active');
  const activeSessionKey = sessions
    .filter((candidate) => ['Queued', 'Dispatched'].includes(candidate.status))
    .slice(0, maximumAutomaticallyRefreshedSessions)
    .map((candidate) => candidate.sessionId)
    .join(',');

  const load = useCallback(
    async (signal?: AbortSignal) => {
      const [nextIdentities, nextSessions] = await Promise.all([
        getSupportIdentities(tenantId, signal),
        getSupportSessions(tenantId, signal),
      ]);
      setIdentities(nextIdentities);
      setSessions(nextSessions);
      const activeIdentities = nextIdentities.filter((identity) => identity.status === 'Active');
      setNodeId((current) =>
        activeIdentities.some((identity) => identity.nodeId === current)
          ? current
          : (activeIdentities[0]?.nodeId ?? ''),
      );
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void load(controller.signal).catch((caught: unknown) => {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        setError(caught instanceof Error ? caught.message : 'Support status could not be loaded.');
      });
    }, 0);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [load]);

  const refreshActiveSessions = useCallback(async () => {
    if (activeSessionKey.length === 0) return;
    sessionRefreshController.current?.abort();
    const controller = new AbortController();
    sessionRefreshController.current = controller;
    try {
      const sessionIds = activeSessionKey.split(',');
      const refreshed = await Promise.all(
        sessionIds.map((sessionId) => getSupportSession(tenantId, sessionId, controller.signal)),
      );
      if (controller.signal.aborted) return;
      const refreshedById = new Map(refreshed.map((candidate) => [candidate.sessionId, candidate]));
      setSessions((current) =>
        current.map((candidate) => refreshedById.get(candidate.sessionId) ?? candidate),
      );
      setSessionRefreshError(null);
    } catch (caught) {
      if (caught instanceof Error && caught.name === 'AbortError') return;
      setSessionRefreshError(
        caught instanceof Error
          ? caught.message
          : 'Active support sessions could not be refreshed automatically.',
      );
    } finally {
      if (sessionRefreshController.current === controller) {
        sessionRefreshController.current = null;
      }
    }
  }, [activeSessionKey, tenantId]);

  useEffect(() => {
    if (activeSessionKey.length === 0) return;
    const refreshTimer = window.setInterval(() => {
      void refreshActiveSessions();
    }, sessionRefreshIntervalMilliseconds);
    return () => {
      sessionRefreshController.current?.abort();
      sessionRefreshController.current = null;
      window.clearInterval(refreshTimer);
    };
  }, [activeSessionKey, refreshActiveSessions]);

  const requestSession = async () => {
    if (!session) return;
    setBusy(true);
    try {
      await createSupportSession(
        tenantId,
        nodeId,
        mode,
        profileId.trim().length === 0 ? null : profileId.trim(),
        session.antiforgeryToken,
      );
      await load();
      setError(null);
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : 'Support diagnostic session could not be created.',
      );
    } finally {
      setBusy(false);
    }
  };

  const enroll = async () => {
    if (!session) return;
    setBusy(true);
    try {
      setEnrollment(
        await createSupportEnrollment(tenantId, displayName.trim(), session.antiforgeryToken),
      );
      await load();
      setError(null);
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'Support identity could not be enrolled.',
      );
    } finally {
      setBusy(false);
    }
  };

  const revokeIdentity = async (identityNodeId: string) => {
    if (!session) return;
    setRevokingNodeId(identityNodeId);
    try {
      await revokeSupportIdentity(tenantId, identityNodeId, session.antiforgeryToken);
      await load();
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Support identity could not be revoked.');
    } finally {
      setRevokingNodeId(null);
    }
  };

  return (
    <section className="grid gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Support diagnostics</h1>
        <p className="text-sm text-muted-foreground">
          Request bounded read-only diagnostics through the independent support agent. This status
          is separate from normal connector and runner health.
        </p>
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
      <Card>
        <CardHeader>
          <CardTitle as="h2">Support identities</CardTitle>
          <CardDescription>
            Active support nodes poll the opaque relay without sharing connector credentials.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3">
          <SupportIdentityInventory
            identities={identities}
            revokingNodeId={revokingNodeId}
            onRevoke={revokeIdentity}
          />
          <fieldset className="grid gap-3 rounded-lg border p-3">
            <legend className="px-2 text-sm font-medium">Create node enrollment</legend>
            <p className="max-w-[70ch] text-sm text-muted-foreground">
              The node generates its private keys locally. Create a one-time code, then provide it
              to the support-agent enrollment configuration before it expires.
            </p>
            <FormField label="Display name" hint="Shown after the node completes enrollment.">
              <input
                className="h-9 rounded-md border bg-background px-3 text-sm"
                maxLength={128}
                required
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
              />
            </FormField>
            <Button
              type="button"
              disabled={busy || displayName.trim().length === 0}
              onClick={() => void enroll()}
            >
              Create one-time code
            </Button>
          </fieldset>
          {enrollment ? (
            <div
              role="status"
              className="grid gap-2 rounded-lg border border-status-caution-foreground/30 bg-status-caution p-4 text-status-caution-foreground"
            >
              <strong>Copy this one-time code now</strong>
              <p className="text-sm">
                It expires {formatTime(enrollment.enrollmentExpiresAt)} and can enroll only one node
                in this tenant.
              </p>
              <span className="text-xs font-medium">One-time enrollment code</span>
              <code className="block overflow-x-auto break-all rounded bg-background p-3 text-xs text-foreground">
                {enrollment.enrollmentCode}
              </code>
            </div>
          ) : null}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle as="h2">Request diagnostic session</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-3">
          {activeIdentities.length > 0 ? (
            <FormField label="Support node">
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={nodeId}
                onChange={(event) => setNodeId(event.target.value)}
              >
                {activeIdentities.map((identity) => (
                  <option key={identity.nodeId} value={identity.nodeId}>
                    {identity.displayName}
                  </option>
                ))}
              </select>
            </FormField>
          ) : (
            <p className="text-sm text-muted-foreground">
              No active support nodes are available. Complete a new node enrollment before
              requesting diagnostics.
            </p>
          )}
          <FormField label="Diagnostic mode">
            <select
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={mode}
              onChange={(event) => setMode(event.target.value as typeof mode)}
            >
              {diagnosticModes.map((candidate) => (
                <option key={candidate}>{candidate}</option>
              ))}
            </select>
          </FormField>
          <FormField label="Profile ID" hint="Optional locally configured profile.">
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={profileId}
              onChange={(event) => setProfileId(event.target.value)}
            />
          </FormField>
          <Button type="button" disabled={busy || !nodeId} onClick={() => void requestSession()}>
            Request read-only diagnostics
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle as="h2">Recent sessions</CardTitle>
          <CardDescription>
            Queued and dispatched sessions update automatically every five seconds and can run for
            up to 15 minutes. You can leave this page and return later.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3">
          {sessionRefreshError ? (
            <p role="alert" className="text-sm text-destructive">
              Automatic session refresh failed: {sessionRefreshError}
            </p>
          ) : null}
          {sessions.map((supportSession) => (
            <SupportSessionCard key={supportSession.sessionId} session={supportSession} />
          ))}
        </CardContent>
      </Card>
    </section>
  );
}

export interface SupportIdentityInventoryProps {
  readonly identities: readonly SupportIdentity[];
  readonly revokingNodeId?: string | null;
  readonly onRevoke?: (nodeId: string) => Promise<void>;
}

export function SupportIdentityInventory({
  identities,
  revokingNodeId = null,
  onRevoke,
}: SupportIdentityInventoryProps) {
  const activeIdentities = identities.filter((identity) => identity.status === 'Active');
  const revokedIdentities = identities.filter((identity) => identity.status === 'Revoked');
  return (
    <>
      {activeIdentities.length > 0 ? (
        <div className="grid gap-2 md:grid-cols-2">
          {activeIdentities.map((identity) => (
            <SupportIdentityCard
              key={identity.nodeId}
              identity={identity}
              revoking={revokingNodeId === identity.nodeId}
              onRevoke={onRevoke}
            />
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          No active support nodes. Create a new node enrollment when a host is ready.
        </p>
      )}
      {revokedIdentities.length > 0 ? (
        <details className="rounded-lg border bg-muted/20 p-3">
          <summary className="cursor-pointer text-sm font-medium">
            Revoked history ({revokedIdentities.length})
          </summary>
          <p className="mt-2 text-sm text-muted-foreground">
            Revoked identities are retained for audit history and cannot poll or receive new
            diagnostic sessions.
          </p>
          <div className="mt-3 grid gap-2 md:grid-cols-2">
            {revokedIdentities.map((identity) => (
              <SupportIdentityCard key={identity.nodeId} identity={identity} />
            ))}
          </div>
        </details>
      ) : null}
    </>
  );
}

export interface SupportIdentityCardProps {
  readonly identity: SupportIdentity;
  readonly revoking?: boolean;
  readonly onRevoke?: (nodeId: string) => Promise<void>;
}

export function SupportIdentityCard({
  identity,
  revoking = false,
  onRevoke,
}: SupportIdentityCardProps) {
  const canRevoke = identity.status === 'Active' && onRevoke !== undefined;
  return (
    <article className="min-w-0 rounded-lg border p-3">
      <div className="flex items-center justify-between gap-3">
        <h3 className="min-w-0 break-words font-medium">{identity.displayName}</h3>
        <StatusBadge status={identity.status.toLowerCase()} />
      </div>
      <div className="mt-2 break-all font-mono text-xs text-muted-foreground">
        {identity.nodeId}
      </div>
      <dl className="mt-2 grid gap-1 text-sm">
        {identity.status === 'Active' ? (
          <>
            <div>
              Last poll: {identity.lastPollAt ? formatTime(identity.lastPollAt) : 'Unavailable'}
            </div>
            <div>
              Last result:{' '}
              {identity.lastResultAt ? formatTime(identity.lastResultAt) : 'Unavailable'}
            </div>
          </>
        ) : (
          <div>
            {identity.revokedAt
              ? `Revoked ${formatTime(identity.revokedAt)}`
              : 'Revocation time unavailable'}
          </div>
        )}
        <div>Capability: v{identity.capabilityVersion}</div>
      </dl>
      {canRevoke ? (
        <div className="mt-3">
          <ConfirmActionDialog
            trigger={
              <Button type="button" variant="outline" size="sm" disabled={revoking}>
                {revoking ? 'Revoking…' : 'Revoke'}
              </Button>
            }
            title={`Revoke "${identity.displayName}"?`}
            description="Revoking immediately prevents this support identity from polling the relay or receiving new diagnostic sessions."
            confirmLabel="Revoke support identity"
            confirmVariant="destructive"
            details={
              <ConfirmationSummary
                identity={[
                  { label: 'Support node', value: identity.displayName },
                  { label: 'Node ID', value: identity.nodeId },
                ]}
                effects={[
                  'Relay polling and new diagnostic sessions are rejected immediately.',
                  'This support identity cannot be reinstated.',
                ]}
                prohibitedEffects={[
                  'The normal connector identity and runner pools are unchanged.',
                  'Local support keys are not removed by this Dashboard action.',
                ]}
              />
            }
            onConfirm={() => onRevoke(identity.nodeId)}
          />
        </div>
      ) : null}
    </article>
  );
}

export interface SupportSessionCardProps {
  readonly session: SupportSession;
}

export function SupportSessionCard({ session }: SupportSessionCardProps) {
  const active = ['Queued', 'Dispatched'].includes(session.status);
  const tone =
    session.status === 'Completed'
      ? 'positive'
      : session.status === 'Queued' || session.status === 'Dispatched'
        ? 'caution'
        : session.status === 'Cancelled'
          ? 'neutral'
          : 'critical';
  const rejectionExplanation = session.rejectionDisposition
    ? (rejectionGuidance[session.rejectionDisposition] ??
      'The support agent rejected this request before producing a verified report.')
    : null;
  return (
    <article className="grid gap-2 rounded-lg border p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">{session.diagnosticMode}</h3>
        <span aria-live="polite">
          <StatusBadge status={session.status} tone={tone} />
        </span>
      </div>
      <div className="break-all font-mono text-xs text-muted-foreground">{session.sessionId}</div>
      <div className="text-sm text-muted-foreground">
        Requested {formatTime(session.requestedAt)} · expires {formatTime(session.expiresAt)}
      </div>
      {session.dispatchedAt ? (
        <div className="text-sm text-muted-foreground">
          First dispatched {formatTime(session.dispatchedAt)}
        </div>
      ) : null}
      {session.rejectionDisposition ? (
        <>
          <p className="text-sm text-muted-foreground">{rejectionExplanation}</p>
          <div className="text-sm text-muted-foreground">
            Technical disposition:{' '}
            <code className="rounded bg-muted px-1 py-0.5 text-xs">
              {session.rejectionDisposition}
            </code>
          </div>
        </>
      ) : null}
      {session.result ? (
        <pre className="overflow-x-auto rounded-md bg-muted p-3 text-xs">
          {JSON.stringify(session.result.report, null, 2)}
        </pre>
      ) : (
        <p className="text-sm text-muted-foreground">
          Verified report unavailable until completion.
        </p>
      )}
      {active ? (
        <p role="status" aria-live="polite" className="text-sm text-muted-foreground">
          Waiting for a terminal result. This session updates automatically.
        </p>
      ) : null}
      {session.result ? (
        <pre className="whitespace-pre-wrap rounded-md bg-muted p-3 text-sm">
          {session.result.markdown}
        </pre>
      ) : null}
      {session.result ? (
        <p className="break-all text-xs text-muted-foreground">
          Attestation {session.result.attestation.signatureAlgorithm}:{' '}
          {session.result.attestation.signatureBase64Url}
        </p>
      ) : null}
    </article>
  );
}
