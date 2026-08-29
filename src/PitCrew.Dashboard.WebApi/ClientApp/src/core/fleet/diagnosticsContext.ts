import { z } from 'zod';

import { offsetDateTimeSchema, type FleetNode, type OperationalIncident } from './fleetApi';

const diagnosticModeSchema = z.enum([
  'ConnectorOffline',
  'CapacityMismatch',
  'JobNotAssigned',
  'HostPressure',
  'Full',
]);

export const diagnosticModes = diagnosticModeSchema.options;
export type DiagnosticMode = z.infer<typeof diagnosticModeSchema>;

const unavailableEvidenceSchema = z.object({
  category: z.string().min(1).max(128),
  reason: z.string().min(1).max(1024),
  followUp: z.string().min(1).max(1024),
});

export const diagnosticsContextSchema = z.object({
  schemaVersion: z.literal(1),
  capturedAt: offsetDateTimeSchema,
  diagnosticMode: diagnosticModeSchema,
  dashboard: z.object({
    nodeId: z.string().uuid(),
    status: z.enum(['online', 'offline', 'revoked']),
    lastSeenAt: offsetDateTimeSchema.nullable(),
    incident: z.string().min(1).max(128).nullable(),
    publicEndpoint: z.null(),
  }),
  github: z.null(),
  releases: z.object({
    pitcrew: z.null(),
    dashboard: z.null(),
  }),
  unavailableEvidence: z.array(unavailableEvidenceSchema).max(16),
});

export type DiagnosticsContext = z.infer<typeof diagnosticsContextSchema>;

/** Builds the schema-bound preflight context consumed by PitCrew remote diagnostics. */
export function buildDiagnosticsContext(
  node: FleetNode,
  generatedAt: string,
  incidents: ReadonlyArray<OperationalIncident>,
): DiagnosticsContext {
  const incident = [...incidents]
    .filter((candidate) => candidate.nodeId === node.nodeId)
    .sort((left, right) => {
      if (left.severity !== right.severity) return left.severity === 'critical' ? -1 : 1;
      return right.lastObservedAt.localeCompare(left.lastObservedAt);
    })[0];
  const diagnosticMode = selectDiagnosticMode(node, incident);
  const unavailableEvidence = [
    {
      category: 'public-dashboard-endpoint',
      reason: 'Dashboard did not package an independent public endpoint probe.',
      followUp: 'Probe the exact query-free Dashboard origin before requesting host evidence.',
    },
    {
      category: 'github-actions',
      reason: 'Dashboard did not package an affected GitHub Actions run or job.',
      followUp: 'Add the exact run or job URL through the PitCrew remote diagnostics preflight.',
    },
    {
      category: 'pitcrew-release',
      reason: 'Dashboard did not package the latest PitCrew release.',
      followUp: 'Read the latest published ncosentino/pitcrew release before host collection.',
    },
    {
      category: 'dashboard-release',
      reason: 'Dashboard did not package its latest published release.',
      followUp:
        'Read the latest published ncosentino/pitcrew-dashboard release before host collection.',
    },
    ...(node.connectorHealth == null
      ? [
          {
            category: 'connector-health-replay',
            reason: 'This node has never replayed bounded connector-health evidence.',
            followUp:
              'Collect the local connector journal with the PitCrew remote diagnostics bundle.',
          },
        ]
      : []),
  ];

  return diagnosticsContextSchema.parse({
    schemaVersion: 1,
    capturedAt: generatedAt,
    diagnosticMode,
    dashboard: {
      nodeId: node.nodeId,
      status: node.isRevoked ? 'revoked' : node.isOnline ? 'online' : 'offline',
      lastSeenAt: node.lastSeenAt,
      incident: node.isOnline
        ? (incident?.reason ?? null)
        : (node.connectorHealth?.snapshot.lastFailureCategory ??
          incident?.reason ??
          'connector-offline'),
      publicEndpoint: null,
    },
    github: null,
    releases: {
      pitcrew: null,
      dashboard: null,
    },
    unavailableEvidence,
  });
}

/** Serializes a deterministic, human-readable diagnostic context document. */
export function serializeDiagnosticsContext(context: DiagnosticsContext): string {
  return `${JSON.stringify(context, null, 2)}\n`;
}

function selectDiagnosticMode(
  node: FleetNode,
  incident: OperationalIncident | undefined,
): DiagnosticsContext['diagnosticMode'] {
  return selectIncidentDiagnosticMode(incident, node);
}

/**
 * Chooses the diagnostic mode that matches an incident, optionally refined by the
 * node it was raised against.
 */
export function selectIncidentDiagnosticMode(
  incident: Pick<OperationalIncident, 'kind'> | undefined,
  node?: Pick<FleetNode, 'isOnline'>,
): DiagnosticMode {
  if (node?.isOnline === false || incident?.kind === 'connector-offline') return 'ConnectorOffline';
  if (incident?.kind === 'capacity-deficit') return 'CapacityMismatch';
  if (incident?.kind.startsWith('resource-')) return 'HostPressure';
  if (incident?.kind.includes('job')) return 'JobNotAssigned';
  return 'Full';
}

/**
 * Builds the tenant support route that requests bounded read-only diagnostics with the
 * given mode preselected. The support node is chosen on that page because support
 * identities are enrolled independently from connector node identity.
 */
export function buildSupportDiagnosticRequestPath(
  tenantId: string,
  mode: DiagnosticMode,
  profileId?: string | null,
): string {
  const query = new URLSearchParams({ mode });
  if (profileId) query.set('profileId', profileId);
  return `/tenants/${encodeURIComponent(tenantId)}/support/run?${query.toString()}`;
}
