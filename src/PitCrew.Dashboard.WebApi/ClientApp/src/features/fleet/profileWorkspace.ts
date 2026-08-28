import {
  describeHostAdmission,
  describeSubsystemHealth,
  summarizeManagerOperations,
  type ManagerObservedState,
  type OperationalIncident,
} from '@/core/fleet';

export type ProfileAttentionTone = 'positive' | 'caution' | 'critical';
export type ProfileAttentionTask = 'overview' | 'capacity' | 'workers' | 'diagnostics';

export interface ProfileAttentionSummary {
  readonly label: string;
  readonly description: string;
  readonly tone: ProfileAttentionTone;
  readonly task: ProfileAttentionTask;
  readonly rank: number;
}

export interface ProfileWorkloadSummary {
  readonly confirmedBusyWorkers: number;
  readonly unknownActivityWorkers: number;
  readonly reportedRunningJobs: number;
  readonly jobReportingProfiles: number;
  readonly busyLabel: string;
  readonly busyDetail: string;
  readonly runningJobsLabel: string;
  readonly runningJobsDetail: string;
}

/** Preserves unknown worker activity and job statistics instead of converting them to zero. */
export function summarizeProfileWorkload(profile: ManagerObservedState): ProfileWorkloadSummary {
  const busyWorkers = profile.slots.filter((slot) => slot.activity === 'busy').length;
  const unknownWorkers = profile.slots.filter(
    (slot) => slot.activity == null || slot.activity === 'unknown',
  ).length;

  return {
    confirmedBusyWorkers: busyWorkers,
    unknownActivityWorkers: unknownWorkers,
    reportedRunningJobs: profile.autoscaling?.runningJobs ?? 0,
    jobReportingProfiles: profile.autoscaling == null ? 0 : 1,
    busyLabel: unknownWorkers > 0 ? `${busyWorkers} confirmed busy` : `${busyWorkers} busy`,
    busyDetail:
      unknownWorkers > 0
        ? `${unknownWorkers} worker ${unknownWorkers === 1 ? 'activity is' : 'activities are'} unavailable`
        : 'Every reported worker activity is classified',
    runningJobsLabel:
      profile.autoscaling == null ? 'Unavailable' : String(profile.autoscaling.runningJobs),
    runningJobsDetail:
      profile.autoscaling == null
        ? 'This profile does not report aggregate running-job statistics'
        : 'Manager-reported running jobs',
  };
}

/** Aggregates only explicitly reported workload evidence across profiles. */
export function summarizeNodeWorkload(
  profiles: ReadonlyArray<ManagerObservedState>,
): ProfileWorkloadSummary {
  const profileSummaries = profiles.map(summarizeProfileWorkload);
  const confirmedBusy = profiles.reduce(
    (count, profile) => count + profile.slots.filter((slot) => slot.activity === 'busy').length,
    0,
  );
  const unknownWorkers = profiles.reduce(
    (count, profile) =>
      count +
      profile.slots.filter((slot) => slot.activity == null || slot.activity === 'unknown').length,
    0,
  );
  const jobReportingProfiles = profiles.filter((profile) => profile.autoscaling != null);
  const reportedRunningJobs = jobReportingProfiles.reduce(
    (count, profile) => count + (profile.autoscaling?.runningJobs ?? 0),
    0,
  );

  return {
    confirmedBusyWorkers: confirmedBusy,
    unknownActivityWorkers: unknownWorkers,
    reportedRunningJobs,
    jobReportingProfiles: jobReportingProfiles.length,
    busyLabel: unknownWorkers > 0 ? `${confirmedBusy} confirmed busy` : `${confirmedBusy} busy`,
    busyDetail:
      unknownWorkers > 0
        ? `${unknownWorkers} worker ${unknownWorkers === 1 ? 'activity is' : 'activities are'} unavailable`
        : profileSummaries.length === 0
          ? 'No profiles reported'
          : 'Every reported worker activity is classified',
    runningJobsLabel:
      jobReportingProfiles.length === 0 ? 'Unavailable' : String(reportedRunningJobs),
    runningJobsDetail:
      profiles.length === 0
        ? 'No profiles report aggregate running-job statistics'
        : jobReportingProfiles.length === profiles.length
          ? 'Every profile reports aggregate running-job statistics'
          : `${jobReportingProfiles.length} of ${profiles.length} profiles report aggregate running-job statistics`,
  };
}

/** Selects the highest-priority explicit exception for profile readiness and inventory ordering. */
export function summarizeProfileAttention(
  profile: ManagerObservedState,
  incidents: ReadonlyArray<OperationalIncident>,
): ProfileAttentionSummary {
  const candidates: ProfileAttentionSummary[] = [];
  const profileIncidents = incidents.filter(
    (incident) => incident.profileId == null || incident.profileId === profile.profileId,
  );
  if (profileIncidents.some((incident) => incident.severity === 'critical')) {
    candidates.push(
      attention(
        'Critical incident',
        'A retained critical incident applies to this profile.',
        'critical',
        'overview',
      ),
    );
  } else if (profileIncidents.length > 0) {
    candidates.push(
      attention(
        `${profileIncidents.length} active ${profileIncidents.length === 1 ? 'incident' : 'incidents'}`,
        'Review retained incident evidence before acting.',
        'caution',
        'overview',
        1,
      ),
    );
  }
  if (profile.managerStatus === 'stopped') {
    candidates.push(
      attention(
        'Manager stopped',
        'Manager observations may not advance.',
        'critical',
        'diagnostics',
      ),
    );
  } else if (
    profile.managerStatus === 'starting' ||
    profile.managerStatus === 'stopping' ||
    profile.managerStatus === 'stale'
  ) {
    candidates.push(
      attention(
        `Manager ${profile.managerStatus}`,
        profile.managerStatus === 'starting'
          ? 'The manager has not reached its running state.'
          : profile.managerStatus === 'stopping'
            ? 'The manager is stopping and observations may stop advancing.'
            : 'Manager observations may not be current.',
        'caution',
        'diagnostics',
        2,
      ),
    );
  }
  if (profile.desiredStateStatus !== 'accepted') {
    candidates.push(
      attention(
        `Desired state ${profile.desiredStateStatus}`,
        'The manager has not accepted the current desired state.',
        'caution',
        'capacity',
        3,
      ),
    );
  }
  if (profile.autoscaling?.status === 'degraded' || profile.autoscaling?.lastError) {
    candidates.push(
      attention(
        'Autoscaling degraded',
        'The manager reports degraded autoscaling evidence.',
        'critical',
        'capacity',
      ),
    );
  }
  if (profile.update?.status === 'degraded') {
    candidates.push(
      attention(
        'Worker rollout degraded',
        'The manager reports a degraded worker-image rollout.',
        'critical',
        'workers',
      ),
    );
  } else if (profile.update?.status === 'rolling') {
    candidates.push(
      attention(
        'Worker rollout active',
        'Current and stale workers coexist while the rollout converges.',
        'caution',
        'workers',
        4,
      ),
    );
  }

  const docker = describeSubsystemHealth(profile.subsystemHealth?.docker, 'Docker');
  const github = describeSubsystemHealth(profile.subsystemHealth?.github, 'GitHub');
  if (docker.status === 'degraded' || github.status === 'degraded') {
    candidates.push(
      attention(
        'Subsystem degraded',
        'Docker or GitHub manager operations report a failure.',
        'critical',
        'diagnostics',
      ),
    );
  }
  if (profile.resourceTelemetry?.status === 'unavailable') {
    candidates.push(
      attention(
        'Telemetry unavailable',
        'Resource evidence is unavailable rather than zero.',
        'caution',
        'diagnostics',
        5,
      ),
    );
  } else if (profile.resourceTelemetry?.status === 'partial') {
    candidates.push(
      attention(
        'Telemetry partial',
        'Resource totals include only reporting sources.',
        'caution',
        'diagnostics',
        5,
      ),
    );
  }

  const hostAdmission = describeHostAdmission(profile.hostAdmission);
  if (hostAdmission.status === 'unavailable') {
    candidates.push(
      attention('Host admission unavailable', hostAdmission.description, 'caution', 'capacity', 6),
    );
  } else if (hostAdmission.status === 'degraded') {
    candidates.push(
      attention('Host admission degraded', hostAdmission.description, 'caution', 'capacity', 6),
    );
  }

  const operations = summarizeManagerOperations(profile.operationJournal);
  if (operations.status === 'degraded') {
    candidates.push(attention(operations.label, operations.description, 'critical', 'diagnostics'));
  } else if (operations.status === 'partial' || operations.status === 'unavailable') {
    candidates.push(
      attention(
        operations.status === 'unavailable' ? 'Manager operations unavailable' : operations.label,
        operations.description,
        'caution',
        'diagnostics',
        7,
      ),
    );
  }

  return (
    candidates.sort((left, right) => left.rank - right.rank)[0] ?? {
      label: 'No reported exception',
      description: 'Current reported lifecycle evidence contains no material exception.',
      tone: 'positive',
      task: 'overview',
      rank: 100,
    }
  );
}

function attention(
  label: string,
  description: string,
  tone: Exclude<ProfileAttentionTone, 'positive'>,
  task: ProfileAttentionTask,
  rank = tone === 'critical' ? 0 : 10,
): ProfileAttentionSummary {
  return {
    label,
    description,
    tone,
    task,
    rank,
  };
}
