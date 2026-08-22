import { useCallback, useEffect, useState } from 'react';
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
  const [busy, setBusy] = useState(false);
  const [refreshingSessionId, setRefreshingSessionId] = useState<string | null>(null);
  const [revokingNodeId, setRevokingNodeId] = useState<string | null>(null);
  const activeIdentities = identities.filter((identity) => identity.status === 'Active');

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

  const checkSession = async (sessionId: string) => {
    setRefreshingSessionId(sessionId);
    try {
      const updated = await getSupportSession(tenantId, sessionId);
      setSessions((current) =>
        current.map((candidate) => (candidate.sessionId === sessionId ? updated : candidate)),
      );
      setError(null);
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'Support diagnostic result could not be loaded.',
      );
    } finally {
      setRefreshingSessionId(null);
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
          <div className="grid gap-2 md:grid-cols-2">
            {identities.map((identity) => (
              <SupportIdentityCard
                key={identity.nodeId}
                identity={identity}
                revoking={revokingNodeId === identity.nodeId}
                onRevoke={revokeIdentity}
              />
            ))}
          </div>
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
        </CardHeader>
        <CardContent className="grid gap-3">
          {sessions.map((supportSession) => (
            <SupportSessionCard
              key={supportSession.sessionId}
              session={supportSession}
              refreshing={refreshingSessionId === supportSession.sessionId}
              onCheckResult={checkSession}
            />
          ))}
        </CardContent>
      </Card>
    </section>
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
        <StatusBadge status={identity.status === 'Active' ? 'healthy' : 'critical'} />
      </div>
      <div className="mt-2 break-all font-mono text-xs text-muted-foreground">
        {identity.nodeId}
      </div>
      <dl className="mt-2 grid gap-1 text-sm">
        <div>
          Last poll: {identity.lastPollAt ? formatTime(identity.lastPollAt) : 'Unavailable'}
        </div>
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
  readonly refreshing?: boolean;
  readonly onCheckResult?: (sessionId: string) => Promise<void>;
}

export function SupportSessionCard({
  session,
  refreshing = false,
  onCheckResult,
}: SupportSessionCardProps) {
  const canCheckResult =
    session.result === null &&
    onCheckResult !== undefined &&
    ['Queued', 'Dispatched', 'Expired'].includes(session.status);
  return (
    <article className="grid gap-2 rounded-lg border p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">{session.diagnosticMode}</h3>
        <StatusBadge
          status={
            session.status === 'Completed'
              ? 'healthy'
              : session.status === 'Rejected'
                ? 'critical'
                : 'warning'
          }
        />
      </div>
      <div className="break-all font-mono text-xs text-muted-foreground">{session.sessionId}</div>
      <div className="text-sm text-muted-foreground">
        Requested {formatTime(session.requestedAt)} · expires {formatTime(session.expiresAt)}
      </div>
      {session.result ? (
        <pre className="overflow-x-auto rounded-md bg-muted p-3 text-xs">
          {JSON.stringify(session.result.report, null, 2)}
        </pre>
      ) : (
        <p className="text-sm text-muted-foreground">
          Verified report unavailable until completion.
        </p>
      )}
      {canCheckResult ? (
        <div>
          <Button
            type="button"
            variant="outline"
            disabled={refreshing}
            onClick={() => void onCheckResult(session.sessionId)}
          >
            {refreshing ? 'Checking result…' : 'Check result'}
          </Button>
        </div>
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
