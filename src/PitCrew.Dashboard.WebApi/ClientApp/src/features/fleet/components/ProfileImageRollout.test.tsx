import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { type FleetNode } from '@/core/fleet';
import { fleetNodeSchema } from '@/core/fleet/fleetApi';
import {
  imageCandidateListSchema,
  imageCandidateSchema,
  type ImageCandidate,
} from '@/core/images/imageCandidatesApi';

import {
  profileImageRolloutControlSchema,
  type ProfileImageRolloutControl,
} from '../imageRolloutApi';
import { ProfileImageRollout } from './ProfileImageRollout';

const nodeId = 'a1235ec4-2a15-4f91-a9e0-811152869a51';
const candidateId = '70300000-0000-4000-8000-000000000003';
const profilePath = `/tenants/local/nodes/${nodeId}/profiles/default/image`;
const currentRevision = 'a'.repeat(64);
const targetDigest = `sha256:${'b'.repeat(64)}`;

afterEach(() => vi.restoreAllMocks());

describe('ProfileImageRollout', () => {
  it('confirms one exact candidate and submits immutable fences with a stable key', async () => {
    const postedRequests: Request[] = [];
    installFetch({ onPost: (request) => postedRequests.push(request) });
    renderRollout('administrator');
    const user = userEvent.setup();

    await screen.findByRole('heading', { name: 'Profile image rollout' });
    expect(screen.getByText('Current profile image')).toBeInTheDocument();
    expect(screen.getByText('Selected candidate')).toBeInTheDocument();
    expect(screen.getByText('Preserved operating contract')).toBeInTheDocument();

    const dialog = await openRolloutDialog(user);
    expect(dialog).toHaveTextContent(targetDigest);
    expect(dialog).toHaveTextContent('No automatic rollback or fleet campaign.');
    expect(within(dialog).getByRole('button', { name: 'Roll out image' })).toBeDisabled();

    await acknowledgeAndConfirmRollout(user, dialog);

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    const request = postedRequests[0];
    if (!request) throw new Error('Expected a rollout POST request.');
    expect(request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f]{8}-[0-9a-f-]{27}$/u);
    expect(request.headers.get('X-PitCrew-Antiforgery')).toBe('antiforgery-token');
    const body = JSON.parse(await request.clone().text()) as Record<string, unknown>;
    expect(body).toMatchObject({
      nodeId,
      profileId: 'default',
      candidateId,
      expectedCurrentWorkerRevision: currentRevision,
      expectedDesiredGeneration: 7,
      expectedDesiredStateHash: 'e'.repeat(64),
    });
    expect(screen.getByRole('status')).toHaveTextContent('is queued');
  });

  it('keeps viewer access read-only while preserving rollout evidence', async () => {
    installFetch();
    renderRollout('viewer');

    await screen.findByRole('heading', { name: 'Profile image rollout' });
    expect(screen.getByText(/Viewer access is read-only/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
    expect(screen.getByText('Compatible')).toBeInTheDocument();
  });

  it('matches connector recipe policy case-insensitively', async () => {
    installFetch({
      control: buildControl({ allowedRecipeIds: ['UBUNTU-RUNNER'] }),
    });
    renderRollout('administrator');

    await screen.findByRole('heading', { name: 'Profile image rollout' });
    expect(screen.getByText('Compatible')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeEnabled();
  });

  it('ages retained capability evidence instead of keeping it fresh forever', async () => {
    installFetch({
      control: buildControl({
        observedStateAgeSeconds: 121,
        observedStateFresh: false,
      }),
    });
    renderRollout('administrator');

    await screen.findByText('Stale evidence');
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
    expect(screen.getAllByText('Current profile evidence is stale.')).not.toHaveLength(0);
  });

  it('does not substitute another candidate for a missing deep link', async () => {
    installFetch({ missingCandidateId: '79900000-0000-4000-8000-000000000099' });
    renderRollout('administrator', `${profilePath}?candidate=79900000-0000-4000-8000-000000000099`);

    await screen.findByText(/selected candidate is not present/i);
    expect(
      screen.getByText('Select a ready registry candidate to compare exact authority.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Selected', { selector: 'a' })).not.toBeInTheDocument();
  });

  it('keeps an indeterminate started outcome distinct from failure and retry safety', async () => {
    installFetch({
      control: buildControl({
        operationActive: false,
        latestCommand: buildCommand({
          status: 'indeterminate',
          failureCategory: 'unknown',
          completedAt: '2026-08-29T09:15:00+00:00',
          resultMessage: 'The started rollout could not be proved.',
        }),
      }),
    });
    renderRollout('administrator');

    await screen.findByText(/cannot prove the started operation's terminal state/i);
    expect(screen.getByText('Indeterminate')).toBeInTheDocument();
    expect(screen.getByText(/never executed automatically again/i)).toBeInTheDocument();
  });

  it('blocks authorization while the connector node is offline', async () => {
    installFetch();
    renderRollout(
      'administrator',
      `${profilePath}?candidate=${candidateId}`,
      buildNode({ isOnline: false }),
    );

    await screen.findByText('Node offline');
    expect(
      within(getAuthorizationSection()).getByText('The connector node is offline.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
  });

  it('surfaces connector-local policy denial as a blocking reason', async () => {
    installFetch({
      control: buildControl({
        rolloutAllowed: false,
        localFailureCategory: 'policy-disabled',
      }),
    });
    renderRollout('administrator');

    await screen.findByText('Not ready');
    expect(
      within(getAuthorizationSection()).getByText('The connector reports policy disabled.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
  });

  it('blocks rollout while the shared profile operation slot is in use', async () => {
    installFetch({
      control: buildControl({ operationActive: true }),
    });
    renderRollout('administrator');

    await screen.findByText('Operation active');
    expect(
      within(getAuthorizationSection()).getByText(
        'Another capacity, recovery, or image operation is active for this profile.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
  });

  it('keeps a rejected mutation visible and retryable', async () => {
    const postedRequests: Request[] = [];
    installFetch({
      onPost: (request) => postedRequests.push(request),
      postFailure: {
        status: 409,
        code: 'image_rollout_stale_fence',
        message: 'Current profile fences changed. Refresh before retrying.',
      },
    });
    renderRollout('administrator');
    const user = userEvent.setup();

    await screen.findByRole('heading', { name: 'Profile image rollout' });
    const dialog = await openRolloutDialog(user);
    await acknowledgeAndConfirmRollout(user, dialog);

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Current profile fences changed. Refresh before retrying.',
    );
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeEnabled();
  });

  it('makes retained evidence read-only after refresh failure', async () => {
    installFetch({ failControlAfter: 1 });
    renderRollout('administrator');
    const user = userEvent.setup();

    await screen.findByRole('heading', { name: 'Profile image rollout' });
    const dialog = await openRolloutDialog(user);
    await acknowledgeAndConfirmRollout(user, dialog);

    await screen.findByText(/Showing retained rollout evidence because refresh failed/);
    expect(screen.getByText('Refresh failed')).toBeInTheDocument();
    expect(
      within(getAuthorizationSection()).getByText(
        'Current rollout evidence could not be refreshed.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
  });

  it('distinguishes missing connector capability from viewer permissions', async () => {
    installFetch({ controlNotFound: true });
    renderRollout('administrator');

    await screen.findByText('Rollout unavailable');
    const connectorSupport = screen.getByText('Connector support').parentElement;
    if (connectorSupport === null) {
      throw new Error('Expected the connector support readiness fact.');
    }
    expect(within(connectorSupport).getByText('Not advertised')).toBeInTheDocument();
    expect(
      within(connectorSupport).getByText('Connector did not advertise rollout for this profile'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
  });
});

function renderRollout(
  role: 'viewer' | 'administrator',
  path = `${profilePath}?candidate=${candidateId}`,
  node = buildNode(),
) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <ProfileImageRollout
        tenantId="local"
        node={node}
        profile={node.profiles[0]}
        canAdminister={role === 'administrator'}
        antiforgeryToken="antiforgery-token"
      />
    </MemoryRouter>,
  );
}

function installFetch({
  control = buildControl(),
  missingCandidateId = null,
  onPost,
  postFailure = null,
  failControlAfter = null,
  controlNotFound = false,
}: {
  readonly control?: ProfileImageRolloutControl;
  readonly missingCandidateId?: string | null;
  readonly onPost?: (request: Request) => void;
  readonly postFailure?: {
    readonly status: number;
    readonly code: string;
    readonly message: string;
  } | null;
  readonly failControlAfter?: number | null;
  readonly controlNotFound?: boolean;
} = {}) {
  const candidate = buildCandidate();
  let controlRequestCount = 0;
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const request = input instanceof Request ? input : new Request(input, init);
    const url = new URL(request.url);
    if (request.method === 'POST' && url.pathname.endsWith('/images/profile-rollouts')) {
      onPost?.(request);
      if (postFailure) {
        return jsonResponse(
          {
            error: {
              code: postFailure.code,
              message: postFailure.message,
            },
          },
          postFailure.status,
        );
      }
      return jsonResponse(
        {
          commandId: '90000000-0000-4000-8000-000000000001',
          status: 'queued',
          statusLocation: `/api/tenants/local/images/profile-rollouts/${nodeId}/default`,
        },
        202,
      );
    }
    if (url.pathname.endsWith(`/images/profile-rollouts/${nodeId}/default`)) {
      controlRequestCount += 1;
      if (controlNotFound) {
        return jsonResponse(
          {
            error: {
              code: 'image_rollout_profile_not_found',
              message: 'Profile rollout capability not found.',
            },
          },
          404,
        );
      }
      if (failControlAfter !== null && controlRequestCount > failControlAfter) {
        return jsonResponse(
          {
            error: {
              code: 'image_rollout_refresh_failed',
              message: 'Connector refresh unavailable.',
            },
          },
          503,
        );
      }
      return jsonResponse(control);
    }
    if (url.pathname.endsWith('/images/candidates')) {
      return jsonResponse(
        imageCandidateListSchema.parse({ candidates: [candidate], truncated: false }),
      );
    }
    if (url.pathname.includes('/images/candidates/')) {
      if (missingCandidateId && url.pathname.endsWith(missingCandidateId)) {
        return jsonResponse(
          { error: { code: 'image_candidate_not_found', message: 'Candidate not found.' } },
          404,
        );
      }
      return jsonResponse(candidate);
    }
    throw new Error(`Unexpected request: ${request.method} ${url.pathname}`);
  });
}

function buildCandidate(): ImageCandidate {
  return imageCandidateSchema.parse({
    candidateId,
    requestId: '70200000-0000-4000-8000-000000000002',
    registrationId: '70100000-0000-4000-8000-000000000001',
    registrationVersion: 1,
    outcome: 'ready',
    recipeId: 'ubuntu-runner',
    sourceRepository: 'example/runner-images',
    sourceCommit: 'c'.repeat(40),
    githubRunId: '98765',
    githubRunApiUrl: 'https://api.github.com/repos/example/runner-images/actions/runs/98765',
    githubRunUrl: 'https://github.com/example/runner-images/actions/runs/98765',
    artifactId: '4567',
    artifactName: 'pitcrew-image-candidate',
    artifactDigest: `sha256:${'d'.repeat(64)}`,
    reportHash: 'f'.repeat(64),
    imageReference: 'ghcr.io/example/runner:candidate',
    digest: targetDigest,
    immutableReference: `ghcr.io/example/runner@${targetDigest}`,
    platform: 'linux/amd64',
    outputMode: 'registry',
    failureCategory: null,
    failureDetail: null,
    createdAt: '2026-08-29T09:00:00+00:00',
    storedAt: '2026-08-29T09:01:00+00:00',
    qualifications: [
      { name: 'image-build', status: 'passed' },
      { name: 'buildkit-digest', status: 'passed' },
      { name: 'registry-digest', status: 'passed' },
      { name: 'oci-manifest', status: 'passed' },
      { name: 'builder-cleanup', status: 'passed' },
    ],
  });
}

function buildControl(
  overrides: Partial<ProfileImageRolloutControl> = {},
): ProfileImageRolloutControl {
  return profileImageRolloutControlSchema.parse({
    nodeId,
    profileId: 'default',
    architecture: 'linux/amd64',
    currentImageReference: 'ghcr.io/example/runner:current',
    currentImageDigest: `sha256:${'1'.repeat(64)}`,
    currentLocalImageId: `sha256:${'2'.repeat(64)}`,
    currentWorkerRevision: currentRevision,
    staticFingerprint: '3'.repeat(64),
    preservedConfigurationFingerprint: '4'.repeat(64),
    routingFingerprint: '5'.repeat(64),
    desiredGeneration: 7,
    desiredStateHash: 'e'.repeat(64),
    allowedRecipeIds: ['ubuntu-runner'],
    rolloutAllowed: true,
    localSchemaSupported: true,
    localFailureCategory: null,
    operationActive: false,
    observedStateAgeSeconds: 5,
    observedStateMaximumAgeSeconds: 120,
    observedStateFresh: true,
    managerConvergenceStatus: 'current',
    currentWorkers: 2,
    staleWorkers: 0,
    latestCommand: null,
    recentCommands: [],
    ...overrides,
  });
}

function buildCommand(
  overrides: Partial<NonNullable<ProfileImageRolloutControl['latestCommand']>> = {},
) {
  return {
    commandId: '90100000-0000-4000-8000-000000000001',
    candidateId,
    recipeId: 'ubuntu-runner',
    targetDigest,
    targetPlatform: 'linux/amd64' as const,
    previousImageReference: null,
    previousImageDigest: null,
    previousWorkerRevision: null,
    status: 'started' as const,
    failureCategory: null,
    requestedByGitHubUserId: '1001',
    requestedAt: '2026-08-29T09:02:00+00:00',
    expiresAt: '2026-08-29T09:32:00+00:00',
    deliveredAt: '2026-08-29T09:02:05+00:00',
    claimedAt: '2026-08-29T09:02:06+00:00',
    startedAt: '2026-08-29T09:02:07+00:00',
    completedAt: null,
    targetWorkerRevision: null,
    managerConvergenceStatus: null,
    currentWorkers: null,
    staleWorkers: null,
    lastError: null,
    resultMessage: null,
    previousCandidateId: null,
    previousRecipeId: null,
    ...overrides,
  };
}

function buildNode(overrides: Partial<FleetNode> = {}): FleetNode {
  return fleetNodeSchema.parse({
    nodeId,
    displayName: 'Build host',
    connectorVersion: '0.13.0',
    enrolledAt: '2026-08-28T09:00:00+00:00',
    lastSeenAt: '2026-08-29T09:01:00+00:00',
    isOnline: true,
    isRevoked: false,
    credentialRotationRequested: false,
    profiles: [
      {
        schemaVersion: 1,
        managerContractVersion: 10,
        profileId: 'default',
        managerInstanceId: 'manager-default',
        managerStatus: 'running',
        observedAt: '2026-08-29T09:01:00+00:00',
        scope: 'repo',
        generation: 7,
        desiredStateHash: 'e'.repeat(64),
        desiredStateStatus: 'accepted',
        desiredSlots: 2,
        configuredSlots: 2,
        activeSlots: 2,
        eligibleSlots: 2,
        drainingSlots: 0,
        resourcePolicy: null,
        operationJournal: null,
        subsystemHealth: null,
        capacityEvidence: null,
        hostAdmission: null,
        update: {
          status: 'current',
          targetImage: 'ghcr.io/example/runner:current',
          targetImageId: `sha256:${'2'.repeat(64)}`,
          targetRevision: currentRevision,
          currentWorkers: 2,
          staleWorkers: 0,
          lastError: null,
        },
        slots: [buildSlot('default-1'), buildSlot('default-2')],
      },
    ],
    capacityControls: [],
    recoveryControls: [],
    ...overrides,
  });
}

function buildSlot(key: string) {
  return {
    key,
    repository: 'https://github.com/example/project',
    desired: true,
    processRunning: true,
    state: 'online',
    failureCount: 0,
    backoffSeconds: 0,
    updatedAt: '2026-08-29T09:01:00+00:00',
    resources: null,
    activity: 'idle',
    target: 'repo:example/project',
    registrationStatus: 'connected',
    imageId: `sha256:${'2'.repeat(64)}`,
    lastExit: null,
    runnerNameHash: null,
    currentJob: null,
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function getAuthorizationSection(): HTMLElement {
  const section = screen.getByRole('heading', { name: 'Authorize changeover' }).closest('section');
  if (section === null) {
    throw new Error('Expected the rollout authorization section.');
  }
  return section;
}

async function openRolloutDialog(user: ReturnType<typeof userEvent.setup>): Promise<HTMLElement> {
  await user.click(screen.getByRole('button', { name: 'Roll out image' }));
  return screen.getByRole('alertdialog', { name: 'Roll out ubuntu-runner?' });
}

async function acknowledgeAndConfirmRollout(
  user: ReturnType<typeof userEvent.setup>,
  dialog: HTMLElement,
): Promise<void> {
  await user.click(
    within(dialog).getByRole('checkbox', {
      name: 'I verified the exact candidate, profile, and current fences for this image-only change.',
    }),
  );
  await user.click(within(dialog).getByRole('button', { name: 'Roll out image' }));
}
