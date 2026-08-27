import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { useSession } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { CopyableId } from '@/core/ui/CopyableId';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { FormField } from '@/core/ui/FormField';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { TaskNavigation } from '@/core/ui/TaskNavigation';

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

const diagnosticModeOptions = [
  {
    value: 'ConnectorOffline',
    label: 'Connector offline',
    description: 'Collect support evidence when normal connector status is unavailable.',
  },
  {
    value: 'CapacityMismatch',
    label: 'Capacity mismatch',
    description: 'Compare desired, acknowledged, and observed runner capacity.',
  },
  {
    value: 'JobNotAssigned',
    label: 'Job not assigned',
    description: 'Collect bounded evidence for a job that is waiting for a runner.',
  },
  {
    value: 'HostPressure',
    label: 'Host pressure',
    description: 'Inspect bounded host and worker resource evidence.',
  },
  {
    value: 'Full',
    label: 'Full diagnostic snapshot',
    description: 'Collect every approved read-only support evidence category.',
  },
] as const;
type DiagnosticMode = (typeof diagnosticModeOptions)[number]['value'];

type SupportSection = 'overview' | 'run' | 'sessions' | 'nodes';

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

const sessionStatusRank: Record<SupportSession['status'], number> = {
  Queued: 0,
  Dispatched: 0,
  Rejected: 1,
  Expired: 1,
  Completed: 2,
  Cancelled: 2,
};

/** Tenant support-plane diagnostics workflow. */
export default function SupportPage() {
  const { tenantId = '' } = useParams();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { session } = useSession();
  const [identities, setIdentities] = useState<readonly SupportIdentity[]>([]);
  const [sessions, setSessions] = useState<readonly SupportSession[]>([]);
  const [nodeId, setNodeId] = useState('');
  const [mode, setMode] = useState<DiagnosticMode>('ConnectorOffline');
  const [profileId, setProfileId] = useState('');
  const [enrollment, setEnrollment] = useState<CreatedSupportEnrollment | null>(null);
  const [displayName, setDisplayName] = useState('Support node');
  const [error, setError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [sessionRefreshError, setSessionRefreshError] = useState<string | null>(null);
  const [requestBusy, setRequestBusy] = useState(false);
  const [enrollmentBusy, setEnrollmentBusy] = useState(false);
  const [showEnrollment, setShowEnrollment] = useState(false);
  const [revokingNodeId, setRevokingNodeId] = useState<string | null>(null);
  const sessionRefreshController = useRef<AbortController | null>(null);
  const activeIdentities = identities.filter((identity) => identity.status === 'Active');
  const activeSessions = sessions.filter((candidate) =>
    ['Queued', 'Dispatched'].includes(candidate.status),
  );
  const attentionSessions = sessions.filter((candidate) =>
    ['Rejected', 'Expired'].includes(candidate.status),
  );
  const latestPollAt = latestTimestamp(activeIdentities.map((identity) => identity.lastPollAt));
  const latestResultAt = latestTimestamp(activeIdentities.map((identity) => identity.lastResultAt));
  const supportBasePath = `/tenants/${tenantId}/support`;
  const section = supportSection(pathname, supportBasePath);
  const selectedSessionId = supportSessionId(pathname, supportBasePath);
  const selectedSession =
    sessions.find((candidate) => candidate.sessionId === selectedSessionId) ?? sessions[0] ?? null;
  const selectedMode =
    diagnosticModeOptions.find((candidate) => candidate.value === mode) ?? diagnosticModeOptions[0];
  const taskNavigationItems = [
    {
      label: 'Overview',
      description: 'Readiness and current attention',
      path: supportBasePath,
    },
    {
      label: 'Run diagnostic',
      description: 'Request bounded read-only evidence',
      path: `${supportBasePath}/run`,
    },
    {
      label: 'Sessions',
      description: 'Follow active and recent requests',
      path: `${supportBasePath}/sessions`,
      badge: activeSessions.length > 0 ? String(activeSessions.length) : undefined,
    },
    {
      label: 'Support nodes',
      description: 'Enrollment and identity lifecycle',
      path: `${supportBasePath}/nodes`,
      badge: activeIdentities.length > 0 ? String(activeIdentities.length) : undefined,
    },
  ] as const;
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
      setSessions(prioritizeSessions(nextSessions));
      setLoaded(true);
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
        setLoaded(true);
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
    setRequestBusy(true);
    try {
      const created = await createSupportSession(
        tenantId,
        nodeId,
        mode,
        profileId.trim().length === 0 ? null : profileId.trim(),
        session.antiforgeryToken,
      );
      await load();
      setError(null);
      navigate(`${supportBasePath}/sessions/${created.sessionId}`);
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : 'Support diagnostic session could not be created.',
      );
    } finally {
      setRequestBusy(false);
    }
  };

  const enroll = async () => {
    if (!session) return;
    setEnrollmentBusy(true);
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
      setEnrollmentBusy(false);
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
    <section className="grid min-w-0 gap-5">
      {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
      <ReadinessSummary
        title="Support readiness"
        description="Support diagnostics use an independent outbound identity and remain separate from connector and runner health."
        status={
          <StatusBadge
            status={activeIdentities.length > 0 ? 'Available' : 'Enrollment required'}
            tone={activeIdentities.length > 0 ? 'positive' : 'caution'}
          />
        }
        items={[
          {
            label: 'Active support nodes',
            value: loaded ? activeIdentities.length : 'Loading…',
            detail: !loaded
              ? 'Checking support identity state'
              : activeIdentities.length > 0
                ? 'Eligible for new diagnostic sessions'
                : 'No node can receive a request',
          },
          {
            label: 'Latest relay poll',
            value: !loaded ? 'Loading…' : latestPollAt ? formatTime(latestPollAt) : 'Unavailable',
            detail: 'Reported by active support identities',
          },
          {
            label: 'Active sessions',
            value: loaded ? activeSessions.length : 'Loading…',
            detail: 'Queued or dispatched',
          },
          {
            label: 'Needs attention',
            value: loaded ? attentionSessions.length : 'Loading…',
            detail: 'Rejected or expired sessions',
          },
        ]}
      />

      <div className="grid min-w-0 gap-5 lg:grid-cols-[15rem_minmax(0,1fr)] lg:items-start">
        <TaskNavigation label="Support tasks" items={taskNavigationItems} />
        <div className="min-w-0">
          {section === 'overview' ? (
            <SupportOverview
              activeIdentities={activeIdentities}
              activeSessions={activeSessions}
              attentionSessions={attentionSessions}
              latestResultAt={latestResultAt}
              supportBasePath={supportBasePath}
            />
          ) : null}
          {section === 'run' ? (
            <DetailPanel
              title="Run a diagnostic"
              description="Choose the problem to investigate, then select the support node that should collect the approved read-only evidence."
            >
              <div className="grid gap-5">
                {activeIdentities.length === 0 ? (
                  <StateBanner tone="caution">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <span>Enroll an active support node before requesting diagnostics.</span>
                      <Button asChild type="button" variant="outline" size="sm">
                        <Link to={`${supportBasePath}/nodes`}>Manage support nodes</Link>
                      </Button>
                    </div>
                  </StateBanner>
                ) : null}
                <div className="grid gap-4 xl:grid-cols-2">
                  <FormField label="Problem to investigate" hint={selectedMode.description}>
                    <select
                      className="h-11 rounded-md border bg-background px-3 text-sm"
                      value={mode}
                      onChange={(event) => setMode(event.target.value as DiagnosticMode)}
                    >
                      {diagnosticModeOptions.map((candidate) => (
                        <option key={candidate.value} value={candidate.value}>
                          {candidate.label}
                        </option>
                      ))}
                    </select>
                  </FormField>
                  <FormField label="Support node">
                    <select
                      className="h-11 rounded-md border bg-background px-3 text-sm"
                      disabled={!loaded || activeIdentities.length === 0}
                      value={nodeId}
                      onChange={(event) => setNodeId(event.target.value)}
                    >
                      {!loaded || activeIdentities.length === 0 ? (
                        <option value="">
                          {loaded ? 'No active support nodes' : 'Loading support nodes…'}
                        </option>
                      ) : null}
                      {activeIdentities.map((identity) => (
                        <option key={identity.nodeId} value={identity.nodeId}>
                          {identity.displayName}
                        </option>
                      ))}
                    </select>
                  </FormField>
                </div>
                <FormField
                  label="Profile ID"
                  hint="Optional. Use only when the diagnostic should target one locally configured profile."
                >
                  <input
                    className="h-11 rounded-md border bg-background px-3 text-sm"
                    value={profileId}
                    onChange={(event) => setProfileId(event.target.value)}
                  />
                </FormField>
                <div className="flex flex-wrap items-center gap-3 border-t pt-4">
                  <Button
                    type="button"
                    disabled={requestBusy || !nodeId}
                    onClick={() => void requestSession()}
                  >
                    {requestBusy ? 'Requesting…' : 'Request read-only diagnostics'}
                  </Button>
                  <p className="text-xs text-muted-foreground">
                    Sessions expire after 15 minutes and update automatically.
                  </p>
                </div>
              </div>
            </DetailPanel>
          ) : null}
          {section === 'sessions' ? (
            <SupportSessionsWorkspace
              sessions={sessions}
              selectedSession={selectedSession}
              supportBasePath={supportBasePath}
              refreshError={sessionRefreshError}
            />
          ) : null}
          {section === 'nodes' ? (
            <DetailPanel
              title="Support nodes"
              description="Support identities are independent from normal connector credentials and runner registration."
              actions={
                <Button
                  type="button"
                  variant={showEnrollment ? 'outline' : 'default'}
                  onClick={() => setShowEnrollment((current) => !current)}
                >
                  {showEnrollment ? 'Close enrollment' : 'Enroll support node'}
                </Button>
              }
            >
              <div className="grid gap-4">
                <SupportIdentityInventory
                  identities={identities}
                  revokingNodeId={revokingNodeId}
                  onRevoke={revokeIdentity}
                />
                {showEnrollment ? (
                  <fieldset className="grid gap-4 rounded-lg border bg-muted/20 p-4">
                    <legend className="px-2 text-sm font-medium">Create node enrollment</legend>
                    <p className="max-w-[70ch] text-sm text-muted-foreground">
                      The node generates its private keys locally. Create a one-time code, then
                      provide it to the support-agent enrollment configuration before it expires.
                    </p>
                    <FormField
                      label="Display name"
                      hint="Shown after the node completes enrollment."
                    >
                      <input
                        className="h-11 rounded-md border bg-background px-3 text-sm"
                        maxLength={128}
                        required
                        value={displayName}
                        onChange={(event) => setDisplayName(event.target.value)}
                      />
                    </FormField>
                    <div>
                      <Button
                        type="button"
                        disabled={enrollmentBusy || displayName.trim().length === 0}
                        onClick={() => void enroll()}
                      >
                        {enrollmentBusy ? 'Creating code…' : 'Create one-time code'}
                      </Button>
                    </div>
                  </fieldset>
                ) : null}
                {enrollment ? (
                  <StateBanner tone="caution">
                    <div className="grid gap-2">
                      <strong>Copy this one-time code now</strong>
                      <p>
                        It expires {formatTime(enrollment.enrollmentExpiresAt)} and can enroll only
                        one node in this tenant.
                      </p>
                      <span className="text-xs font-medium">One-time enrollment code</span>
                      <code className="block overflow-x-auto break-all rounded bg-background p-3 text-xs text-foreground">
                        {enrollment.enrollmentCode}
                      </code>
                    </div>
                  </StateBanner>
                ) : null}
              </div>
            </DetailPanel>
          ) : null}
        </div>
      </div>
    </section>
  );
}

interface SupportSessionsWorkspaceProps {
  readonly sessions: ReadonlyArray<SupportSession>;
  readonly selectedSession: SupportSession | null;
  readonly supportBasePath: string;
  readonly refreshError: string | null;
}

function SupportSessionsWorkspace({
  sessions,
  selectedSession,
  supportBasePath,
  refreshError,
}: SupportSessionsWorkspaceProps) {
  return (
    <section aria-labelledby="support-sessions-heading" className="grid min-w-0 gap-4">
      <div>
        <h2 id="support-sessions-heading" className="text-xl font-semibold">
          Support sessions
        </h2>
        <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
          Active requests update every five seconds. Select one session to investigate its lifecycle
          and bounded evidence.
        </p>
      </div>
      {refreshError ? (
        <StateBanner tone="critical">Automatic session refresh failed: {refreshError}</StateBanner>
      ) : null}
      {sessions.length === 0 ? (
        <div className="rounded-xl border bg-card p-5 text-sm text-muted-foreground">
          No support sessions have been requested for this tenant.
        </div>
      ) : (
        <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(18rem,0.85fr)_minmax(0,1.15fr)] xl:items-start">
          <OperationalList label="Support sessions">
            {sessions.map((candidate) => (
              <SupportSessionRow
                key={candidate.sessionId}
                session={candidate}
                selected={candidate.sessionId === selectedSession?.sessionId}
                supportBasePath={supportBasePath}
              />
            ))}
          </OperationalList>
          {selectedSession ? <SupportSessionCard session={selectedSession} /> : null}
        </div>
      )}
    </section>
  );
}

interface SupportSessionRowProps {
  readonly session: SupportSession;
  readonly selected: boolean;
  readonly supportBasePath: string;
}

function SupportSessionRow({ session, selected, supportBasePath }: SupportSessionRowProps) {
  const active = isActiveSession(session);
  return (
    <OperationalRow
      title={diagnosticModeLabel(session.diagnosticMode)}
      description={
        <>
          Requested {formatTime(session.requestedAt)}
          {session.profileId ? ` · Profile ${session.profileId}` : ''}
        </>
      }
      selected={selected}
      status={<StatusBadge status={session.status} tone={sessionTone(session)} />}
      metadata={
        active ? (
          <span className="text-xs text-muted-foreground">
            Waiting for a terminal result · updates automatically
          </span>
        ) : session.rejectionDisposition ? (
          <span className="text-xs text-muted-foreground">
            {rejectionGuidance[session.rejectionDisposition] ??
              'The request ended without a verified report.'}
          </span>
        ) : null
      }
      actions={
        <Button asChild type="button" variant={selected ? 'secondary' : 'outline'} size="sm">
          <Link
            aria-current={selected ? 'page' : undefined}
            to={`${supportBasePath}/sessions/${session.sessionId}`}
          >
            {selected ? 'Selected' : 'View details'}
          </Link>
        </Button>
      }
    />
  );
}

interface SupportOverviewProps {
  readonly activeIdentities: ReadonlyArray<SupportIdentity>;
  readonly activeSessions: ReadonlyArray<SupportSession>;
  readonly attentionSessions: ReadonlyArray<SupportSession>;
  readonly latestResultAt: string | null;
  readonly supportBasePath: string;
}

function SupportOverview({
  activeIdentities,
  activeSessions,
  attentionSessions,
  latestResultAt,
  supportBasePath,
}: SupportOverviewProps) {
  return (
    <DetailPanel
      title="Support workspace"
      description="Start with the current exception, follow live requests, and keep identity administration separate from routine diagnostics."
    >
      <OperationalList label="Support workflow">
        <OperationalRow
          title="Run a diagnostic"
          description={
            activeIdentities.length > 0
              ? `${activeIdentities.length} active support ${activeIdentities.length === 1 ? 'node is' : 'nodes are'} available.`
              : 'Enrollment is required before a node can receive diagnostics.'
          }
          status={
            <StatusBadge
              status={activeIdentities.length > 0 ? 'Ready' : 'Blocked'}
              tone={activeIdentities.length > 0 ? 'positive' : 'caution'}
            />
          }
          actions={
            <Button asChild type="button">
              <Link to={`${supportBasePath}/run`}>
                {activeIdentities.length > 0 ? 'Start diagnostic' : 'View requirements'}
              </Link>
            </Button>
          }
        />
        <OperationalRow
          title="Follow support sessions"
          description={
            activeSessions.length > 0
              ? `${activeSessions.length} session ${activeSessions.length === 1 ? 'is' : 'are'} still running.`
              : attentionSessions.length > 0
                ? `${attentionSessions.length} recent session ${attentionSessions.length === 1 ? 'requires' : 'require'} attention.`
                : latestResultAt
                  ? `Latest verified result ${formatTime(latestResultAt)}.`
                  : 'No verified result is available yet.'
          }
          status={
            activeSessions.length > 0 ? (
              <StatusBadge status="Active" tone="caution" />
            ) : attentionSessions.length > 0 ? (
              <StatusBadge status="Needs attention" tone="critical" />
            ) : (
              <StatusBadge status="No active session" tone="neutral" />
            )
          }
          actions={
            <Button asChild type="button" variant="outline">
              <Link to={`${supportBasePath}/sessions`}>View sessions</Link>
            </Button>
          }
        />
        <OperationalRow
          title="Manage support nodes"
          description="Enroll a new support identity, review node evidence, or revoke access."
          actions={
            <Button asChild type="button" variant="outline">
              <Link to={`${supportBasePath}/nodes`}>Manage nodes</Link>
            </Button>
          }
        />
      </OperationalList>
    </DetailPanel>
  );
}

function supportSection(pathname: string, supportBasePath: string): SupportSection {
  const suffix = pathname.slice(supportBasePath.length).replace(/^\/+|\/+$/g, '');
  if (suffix === 'run') return 'run';
  if (suffix === 'nodes') return 'nodes';
  if (suffix === 'sessions' || suffix.startsWith('sessions/')) return 'sessions';
  return 'overview';
}

function latestTimestamp(values: ReadonlyArray<string | null>): string | null {
  return values.reduce<string | null>((latest, candidate) => {
    if (candidate === null) return latest;
    if (latest === null) return candidate;
    return Date.parse(candidate) > Date.parse(latest) ? candidate : latest;
  }, null);
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
        <OperationalList label="Active support nodes">
          {activeIdentities.map((identity) => (
            <SupportIdentityCard
              key={identity.nodeId}
              identity={identity}
              revoking={revokingNodeId === identity.nodeId}
              onRevoke={onRevoke}
            />
          ))}
        </OperationalList>
      ) : (
        <div className="rounded-lg border bg-muted/20 p-4 text-sm text-muted-foreground">
          No active support nodes. Create a new node enrollment when a host is ready.
        </div>
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
          <OperationalList className="mt-3" label="Revoked support nodes">
            {revokedIdentities.map((identity) => (
              <SupportIdentityCard key={identity.nodeId} identity={identity} />
            ))}
          </OperationalList>
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
    <OperationalRow
      title={identity.displayName}
      description={
        identity.status === 'Active'
          ? `Last poll ${identity.lastPollAt ? formatTime(identity.lastPollAt) : 'unavailable'} · Last result ${
              identity.lastResultAt ? formatTime(identity.lastResultAt) : 'unavailable'
            }`
          : identity.revokedAt
            ? `Revoked ${formatTime(identity.revokedAt)}`
            : 'Revocation time unavailable'
      }
      status={<StatusBadge status={identity.status.toLowerCase()} />}
      actions={
        canRevoke ? (
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
        ) : null
      }
    >
      <details className="text-xs text-muted-foreground">
        <summary className="min-h-6 w-fit cursor-pointer font-medium text-foreground">
          Identity details
        </summary>
        <div className="mt-2 grid gap-2">
          <CopyableId value={identity.nodeId} label={`${identity.displayName} node ID`} />
          <span>Capability version {identity.capabilityVersion}</span>
        </div>
      </details>
    </OperationalRow>
  );
}

export interface SupportSessionCardProps {
  readonly session: SupportSession;
}

export function SupportSessionCard({ session }: SupportSessionCardProps) {
  const active = isActiveSession(session);
  const rejectionExplanation = session.rejectionDisposition
    ? (rejectionGuidance[session.rejectionDisposition] ??
      'The support agent rejected this request before producing a verified report.')
    : null;
  return (
    <DetailPanel
      title={diagnosticModeLabel(session.diagnosticMode)}
      description={`Requested ${formatTime(session.requestedAt)}`}
      status={
        <span aria-live="polite">
          <StatusBadge status={session.status} tone={sessionTone(session)} />
        </span>
      }
    >
      <div className="grid min-w-0 gap-4">
        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <SessionFact label="Expires" value={formatTime(session.expiresAt)} />
          <SessionFact
            label="First dispatched"
            value={session.dispatchedAt ? formatTime(session.dispatchedAt) : 'Not dispatched'}
          />
          <SessionFact label="Profile" value={session.profileId ?? 'All configured profiles'} />
          <SessionFact label="Capability" value={session.capability} />
        </dl>
        <CopyableId value={session.sessionId} label="session ID" prefix="Session" />
        {session.rejectionDisposition ? (
          <StateBanner tone="critical">
            <div className="grid gap-2">
              <strong>{rejectionExplanation}</strong>
              <span>
                Technical disposition:{' '}
                <code className="rounded bg-background/70 px-1 py-0.5 text-xs">
                  {session.rejectionDisposition}
                </code>
              </span>
            </div>
          </StateBanner>
        ) : null}
        {active ? (
          <StateBanner tone="caution" role="status" aria-live="polite">
            Waiting for a terminal result. This session updates automatically.
          </StateBanner>
        ) : null}
        {session.result ? (
          <>
            <section aria-labelledby="verified-report-heading" className="grid min-w-0 gap-2">
              <h3 id="verified-report-heading" className="text-base font-semibold">
                Verified report
              </h3>
              <pre className="max-h-[32rem] overflow-auto whitespace-pre-wrap break-words rounded-lg bg-muted p-4 text-sm">
                {session.result.markdown}
              </pre>
            </section>
            <details className="min-w-0 rounded-lg border bg-muted/20 p-3">
              <summary className="min-h-6 cursor-pointer text-sm font-medium">
                Structured report and attestation
              </summary>
              <div className="mt-3 grid min-w-0 gap-3">
                <pre className="max-h-80 overflow-auto rounded-md bg-muted p-3 text-xs">
                  {JSON.stringify(session.result.report, null, 2)}
                </pre>
                <dl className="grid gap-2 text-xs text-muted-foreground">
                  <SessionFact
                    label="Signature algorithm"
                    value={session.result.attestation.signatureAlgorithm}
                  />
                  <SessionFact label="Node signing key" value={session.nodeSigningKeyFingerprint} />
                  <SessionFact label="Request digest" value={session.requestDigest} />
                </dl>
              </div>
            </details>
          </>
        ) : !active && !session.rejectionDisposition ? (
          <p className="rounded-lg border bg-muted/20 p-4 text-sm text-muted-foreground">
            Verified report unavailable for this terminal session.
          </p>
        ) : null}
      </div>
    </DetailPanel>
  );
}

function SessionFact({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 break-words text-foreground">{value}</dd>
    </div>
  );
}

function isActiveSession(session: SupportSession): boolean {
  return session.status === 'Queued' || session.status === 'Dispatched';
}

function sessionTone(session: SupportSession): 'positive' | 'caution' | 'neutral' | 'critical' {
  if (session.status === 'Completed') return 'positive';
  if (isActiveSession(session)) return 'caution';
  if (session.status === 'Cancelled') return 'neutral';
  return 'critical';
}

function diagnosticModeLabel(mode: string): string {
  return diagnosticModeOptions.find((candidate) => candidate.value === mode)?.label ?? mode;
}

function prioritizeSessions(
  sessions: ReadonlyArray<SupportSession>,
): ReadonlyArray<SupportSession> {
  return [...sessions].sort((left, right) => {
    const rankDifference = sessionStatusRank[left.status] - sessionStatusRank[right.status];
    if (rankDifference !== 0) return rankDifference;
    return Date.parse(right.requestedAt) - Date.parse(left.requestedAt);
  });
}

function supportSessionId(pathname: string, supportBasePath: string): string | null {
  const prefix = `${supportBasePath}/sessions/`;
  return pathname.startsWith(prefix) ? pathname.slice(prefix.length).split('/')[0] || null : null;
}
