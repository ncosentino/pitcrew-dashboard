import { Link } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import {
  buildSupportDiagnosticRequestPath,
  selectIncidentDiagnosticMode,
  type FleetNode,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { OperationalIncident } from '../incidentsApi';

export type IncidentEnrichmentStatus = 'loading' | 'available' | 'stale' | 'unavailable';

interface IncidentDetailProps {
  readonly incident: OperationalIncident;
  readonly node?: FleetNode;
  readonly tenantId: string;
  readonly enrichmentStatus: IncidentEnrichmentStatus;
  readonly isVisible: boolean;
  readonly canAcknowledge: boolean;
  readonly canRequestSupportDiagnostics: boolean;
  readonly isAcknowledging: boolean;
  readonly onAcknowledge: () => void;
  readonly onUnacknowledge: () => void;
}

/** Presents the selected incident as one evidence-led investigation. */
export function IncidentDetail({
  incident,
  node,
  tenantId,
  enrichmentStatus,
  isVisible,
  canAcknowledge,
  canRequestSupportDiagnostics,
  isAcknowledging,
  onAcknowledge,
  onUnacknowledge,
}: IncidentDetailProps) {
  const supportDiagnosticMode = selectIncidentDiagnosticMode(incident, node);
  const connector = node?.connectorHealth?.snapshot;
  const connectorEvidenceIsIncidentSpecific = incident.kind === 'connector-offline';
  const connectorHeading = connectorEvidenceIsIncidentSpecific
    ? 'Connector recovery evidence'
    : 'Node connector context';
  const evidenceHeading = incident.status === 'resolved' ? 'Retained evidence' : 'Current evidence';
  const nodeValue = node
    ? node.displayName
    : enrichmentStatus === 'loading'
      ? 'Node identity loading…'
      : enrichmentStatus === 'unavailable'
        ? 'Node identity unavailable'
        : 'Node not present';
  const nodeDetail = node
    ? node.nodeId
    : enrichmentStatus === 'loading'
      ? 'Fleet enrichment is still loading for this incident.'
      : enrichmentStatus === 'unavailable'
        ? 'Fleet enrichment could not be loaded for this incident.'
        : 'The referenced node is not present in the latest accepted fleet projection.';
  const missingConnectorMessage =
    enrichmentStatus === 'loading'
      ? `${connectorHeading} is still loading.`
      : enrichmentStatus === 'unavailable'
        ? `${connectorHeading} could not be loaded for this incident.`
        : node == null
          ? `The referenced node is not present in the accepted fleet projection, so ${connectorHeading.toLowerCase()} cannot be shown.`
          : `No ${connectorHeading.toLowerCase()} was reported for this node.`;
  return (
    <DetailPanel
      title={incident.title}
      description={incident.summary}
      status={
        <>
          <StatusBadge status={incident.severity} />
          <StatusBadge status={incident.status} />
        </>
      }
      actions={
        <>
          {canRequestSupportDiagnostics && incident.status !== 'resolved' ? (
            <Button asChild size="sm">
              <Link
                data-testid={`incident-request-support-${incident.incidentId}`}
                to={buildSupportDiagnosticRequestPath(
                  tenantId,
                  supportDiagnosticMode,
                  incident.profileId,
                )}
              >
                Request support diagnostics
              </Link>
            </Button>
          ) : null}
          <Button asChild size="sm" variant="outline">
            <Link to={incident.link}>Open owning evidence</Link>
          </Button>
        </>
      }
    >
      <div className="grid min-w-0 gap-5">
        {!isVisible ? (
          <StateBanner tone="caution" role="status">
            This deep-linked incident is outside the current queue filters. Its case file remains
            selected so the investigation does not lose context.
          </StateBanner>
        ) : null}
        {enrichmentStatus === 'stale' ? (
          <StateBanner tone="caution" role="status">
            Showing the last accepted node and connector enrichment because its latest refresh
            failed.
          </StateBanner>
        ) : null}

        <section aria-labelledby={`incident-evidence-${incident.incidentId}`}>
          <h3 id={`incident-evidence-${incident.incidentId}`} className="text-sm font-semibold">
            {evidenceHeading}
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <IncidentFact label="Node" value={nodeValue} detail={nodeDetail} />
            <IncidentFact
              label="Profile"
              value={incident.profileId ?? 'Node-scoped incident'}
              detail={incident.kind}
            />
            <IncidentFact label="Reason" value={incident.reason} />
            <IncidentFact
              label="Evidence"
              value={incident.evidence ?? 'No additional evidence was reported.'}
            />
          </dl>
        </section>

        <section aria-labelledby={`incident-timeline-${incident.incidentId}`}>
          <h3 id={`incident-timeline-${incident.incidentId}`} className="text-sm font-semibold">
            Lifecycle timeline
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <IncidentFact label="First observed" value={formatTime(incident.firstObservedAt)} />
            <IncidentFact label="Triggered" value={formatTime(incident.triggeredAt)} />
            <IncidentFact label="Last observed" value={formatTime(incident.lastObservedAt)} />
            <IncidentFact
              label={incident.status === 'resolved' ? 'Resolved' : 'Ownership'}
              value={
                incident.status === 'resolved'
                  ? incident.resolvedAt
                    ? formatTime(incident.resolvedAt)
                    : 'Resolution time unavailable'
                  : incident.status === 'acknowledged'
                    ? incident.acknowledgedAt
                      ? `Acknowledged ${formatTime(incident.acknowledgedAt)}`
                      : 'Acknowledged; time unavailable'
                    : 'Unacknowledged'
              }
              detail={
                incident.status === 'acknowledged'
                  ? incident.acknowledgedByGitHubUserId
                    ? `GitHub user ${incident.acknowledgedByGitHubUserId}`
                    : 'Owner identity unavailable'
                  : null
              }
            />
          </dl>
        </section>

        <section aria-labelledby={`incident-connector-${incident.incidentId}`}>
          <div className="flex flex-wrap items-center gap-2">
            <h3 id={`incident-connector-${incident.incidentId}`} className="text-sm font-semibold">
              {connectorHeading}
            </h3>
            {connector ? <StatusBadge status={connector.state} /> : null}
          </div>
          {!connectorEvidenceIsIncidentSpecific ? (
            <p className="mt-1 text-sm text-muted-foreground">
              This node-level connector evidence is investigation context, not proof of this
              incident&apos;s cause.
            </p>
          ) : null}
          {connector ? (
            <dl className="mt-3 grid gap-3 sm:grid-cols-2">
              <IncidentFact
                label="Latest connector report"
                value={formatTime(node?.connectorHealth?.receivedAt ?? null)}
              />
              <IncidentFact
                label="Last successful report"
                value={formatTime(connector.lastSuccessAt)}
              />
              <IncidentFact
                label="Latest failure"
                value={connector.lastFailureCategory ?? 'No failure category reported'}
                detail={connector.lastFailureDetail}
              />
              <IncidentFact
                label="Most recent recovery"
                value={
                  connector.lastRecoveredAt
                    ? formatTime(connector.lastRecoveredAt)
                    : 'No recovered outage reported'
                }
                detail={connector.lastRecoveredFailureCategory}
              />
            </dl>
          ) : (
            <div
              className="mt-3 rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground"
              role="status"
            >
              {missingConnectorMessage} Missing or pending enrichment is not treated as healthy
              state.
            </div>
          )}
        </section>

        {canRequestSupportDiagnostics && incident.status !== 'resolved' ? (
          <section
            aria-labelledby={`incident-support-${incident.incidentId}`}
            className="rounded-lg border bg-muted/30 p-4"
          >
            <h3 id={`incident-support-${incident.incidentId}`} className="text-sm font-semibold">
              Independent support evidence
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              A support node collects bounded read-only evidence over an outbound identity that is
              separate from connector reporting, so it stays available while this node&apos;s
              connector does not. Requesting starts the{' '}
              <span className="font-medium text-foreground">{supportDiagnosticMode}</span>{' '}
              diagnostic and changes no node, runner, or host state.
            </p>
            <p className="mt-2 text-sm text-muted-foreground">
              Support identities are enrolled separately from connector node identity. Confirm which
              enrolled support node should collect the evidence before requesting.
            </p>
          </section>
        ) : null}

        {canAcknowledge && incident.status !== 'resolved' ? (
          <section
            aria-labelledby={`incident-ownership-${incident.incidentId}`}
            className="rounded-lg border bg-muted/30 p-4"
          >
            <h3 id={`incident-ownership-${incident.incidentId}`} className="text-sm font-semibold">
              Operator ownership
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Acknowledgement records that an operator owns the investigation. It does not resolve
              or suppress the underlying condition.
            </p>
            <Button
              type="button"
              size="sm"
              variant={incident.status === 'triggered' ? 'default' : 'outline'}
              className="mt-3 min-h-11"
              disabled={isAcknowledging}
              onClick={incident.status === 'triggered' ? onAcknowledge : onUnacknowledge}
            >
              {isAcknowledging
                ? incident.status === 'triggered'
                  ? 'Acknowledging…'
                  : 'Reverting…'
                : incident.status === 'triggered'
                  ? 'Acknowledge incident'
                  : 'Unacknowledge incident'}
            </Button>
          </section>
        ) : null}
      </div>
    </DetailPanel>
  );
}

interface IncidentFactProps {
  readonly label: string;
  readonly value: string;
  readonly detail?: string | null;
}

function IncidentFact({ label, value, detail }: IncidentFactProps) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words text-sm font-medium text-foreground">{value}</dd>
      {detail ? (
        <dd className="mt-0.5 break-words text-xs text-muted-foreground">{detail}</dd>
      ) : null}
    </div>
  );
}
