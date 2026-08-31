import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { imageCandidateSchema, type ImageCandidate } from './imagesApi';
import {
  imageCampaignSchema,
  imageCampaignSummarySchema,
  imageCampaignTargetSchema,
  type ImageCampaign,
  type ImageCampaignTarget,
} from './imageCampaignApi';
import ImageCampaignsPage from './ImageCampaignsPage';
import type { ImageWorkspaceContext } from './imageWorkspaceContext';

const tenantId = 'local';
const candidateId = '70300000-0000-4000-8000-000000000003';
const campaignId = '71000000-0000-4000-8000-000000000001';
const rollbackCampaignId = '71000000-0000-4000-8000-000000000002';
const targetId = '71100000-0000-4000-8000-000000000001';

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('ImageCampaignsPage', () => {
  it('keeps campaign planning and approval read-only for viewers', async () => {
    const campaign = buildCampaign({
      status: 'awaiting-approval',
      revision: 1,
      waveSize: 10,
      configuredByGitHubUserId: '1001',
      configuredAt: '2026-08-29T12:04:00+00:00',
      targets: [buildTarget({ waveNumber: 0, isCanary: true })],
      waves: [
        {
          waveNumber: 0,
          status: 'pending',
          targetCount: 1,
          approvedByGitHubUserId: null,
          approvedAt: null,
          completedAt: null,
        },
      ],
    });
    installFetch({ campaign });
    renderPage('viewer', `/tenants/${tenantId}/images/campaigns/${campaignId}`);

    await screen.findByRole('heading', { name: 'ubuntu-runner campaign' });
    expect(screen.getAllByText(/Viewer access is read-only/)).toHaveLength(2);
    expect(
      screen.getByText(
        'Pause and resume change future campaign dispatch only. Existing profile commands continue to terminal evidence.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve wave' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pause' })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Cancel future dispatch' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Excluded targets' })).toBeInTheDocument();
  });

  it('creates one frozen draft from the selected ready candidate', async () => {
    const postedRequests: Request[] = [];
    const campaign = buildCampaign();
    installFetch({
      campaign,
      onPost: (request) => postedRequests.push(request),
    });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns`);
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'Freeze campaign plan' }));

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    const request = postedRequests[0];
    if (!request) throw new Error('Expected a campaign create request.');
    expect(new URL(request.url).pathname).toBe(`/api/tenants/${tenantId}/images/campaigns`);
    expect(await request.clone().json()).toEqual({ candidateId });
    expect(request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/u);
    expect(request.headers.get('X-PitCrew-Antiforgery')).toBe('antiforgery-token');
    expect(await screen.findByRole('heading', { name: 'ubuntu-runner campaign' })).toBeVisible();
  });

  it('freezes one canary and bounded wave size against the current revision', async () => {
    const postedRequests: Request[] = [];
    const draft = buildCampaign();
    const configured = buildCampaign({
      status: 'awaiting-approval',
      revision: 1,
      waveSize: 10,
      configuredByGitHubUserId: '1001',
      configuredAt: '2026-08-29T12:04:00+00:00',
      targets: [buildTarget({ waveNumber: 0, isCanary: true })],
      waves: [
        {
          waveNumber: 0,
          status: 'pending',
          targetCount: 1,
          approvedByGitHubUserId: null,
          approvedAt: null,
          completedAt: null,
        },
      ],
    });
    installFetch({
      campaign: draft,
      configuredCampaign: configured,
      onPost: (request) => postedRequests.push(request),
    });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns/${campaignId}`);
    const user = userEvent.setup();

    await screen.findByRole('heading', { name: 'Freeze canary and waves' });
    await user.click(screen.getByRole('button', { name: 'Freeze campaign waves' }));
    const dialog = screen.getByRole('alertdialog', {
      name: 'Freeze canary and wave assignment?',
    });
    const requestKey = within(dialog).getByText('Request key').parentElement;
    if (requestKey === null) throw new Error('Expected the configuration request key.');
    expect(within(requestKey).getByText(/^[0-9a-f-]{36}$/u)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Freeze campaign waves' })).toBeDisabled();
    await user.click(
      within(dialog).getByRole('checkbox', {
        name: 'I reviewed the immutable canary, wave size, target set, and campaign revision.',
      }),
    );
    await user.click(within(dialog).getByRole('button', { name: 'Freeze campaign waves' }));

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    const request = postedRequests[0];
    if (!request) throw new Error('Expected a campaign configure request.');
    expect(new URL(request.url).pathname).toBe(
      `/api/tenants/${tenantId}/images/campaigns/${campaignId}/configure`,
    );
    expect(await request.clone().json()).toEqual({
      canaryTargetId: null,
      waveSize: 1,
      expectedRevision: 0,
      expectedTargetSetHash: 'f'.repeat(64),
    });
    expect(await screen.findByRole('heading', { name: 'Approve canary' })).toBeVisible();
  });

  it('requires acknowledgement before approving one exact wave', async () => {
    const postedRequests: Request[] = [];
    const awaiting = buildCampaign({
      status: 'awaiting-approval',
      revision: 1,
      waveSize: 10,
      configuredByGitHubUserId: '1001',
      configuredAt: '2026-08-29T12:04:00+00:00',
      targets: [buildTarget({ waveNumber: 0, isCanary: true })],
      waves: [
        {
          waveNumber: 0,
          status: 'pending',
          targetCount: 1,
          approvedByGitHubUserId: null,
          approvedAt: null,
          completedAt: null,
        },
      ],
    });
    const running = buildCampaign({
      ...awaiting,
      status: 'running',
      revision: 2,
      targets: [buildTarget({ waveNumber: 0, isCanary: true, status: 'queued' })],
      waves: [
        {
          waveNumber: 0,
          status: 'approved',
          targetCount: 1,
          approvedByGitHubUserId: '1001',
          approvedAt: '2026-08-29T12:05:00+00:00',
          completedAt: null,
        },
      ],
    });
    installFetch({
      campaign: awaiting,
      approvedCampaign: running,
      onPost: (request) => postedRequests.push(request),
    });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns/${campaignId}`);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Approve wave' }));
    const dialog = screen.getByRole('alertdialog', { name: 'Approve canary?' });
    expect(within(dialog).getByRole('button', { name: 'Approve wave' })).toBeDisabled();
    expect(within(dialog).getByText(candidateId)).toBeInTheDocument();
    expect(within(dialog).getByText(`sha256:${'e'.repeat(64)}`)).toBeInTheDocument();
    expect(within(dialog).getByText('linux/amd64')).toBeInTheDocument();
    expect(
      within(dialog).getByText(`Alpha · a1235ec4-2a15-4f91-a9e0-811152869a51 · build`),
    ).toBeInTheDocument();
    const requestKey = within(dialog).getByText('Request key').parentElement;
    if (requestKey === null) throw new Error('Expected the approval request key.');
    expect(within(requestKey).getByText(/^[0-9a-f-]{36}$/u)).toBeInTheDocument();
    await user.click(
      within(dialog).getByRole('checkbox', {
        name: 'I reviewed the exact target set, candidate authority, and current campaign revision.',
      }),
    );
    await user.click(within(dialog).getByRole('button', { name: 'Approve wave' }));

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    const request = postedRequests[0];
    if (!request) throw new Error('Expected a campaign approval request.');
    expect(new URL(request.url).pathname).toBe(
      `/api/tenants/${tenantId}/images/campaigns/${campaignId}/waves/0/approve`,
    );
    expect(await request.clone().json()).toEqual({
      expectedRevision: 1,
      expectedTargetSetHash: 'f'.repeat(64),
    });
    expect(await screen.findByText('Running')).toBeVisible();
  });

  it('confirms pause with a stable request key and continuation boundary', async () => {
    const postedRequests: Request[] = [];
    const running = buildCampaign({
      status: 'running',
      revision: 3,
      waveSize: 10,
      configuredByGitHubUserId: '1001',
      configuredAt: '2026-08-29T12:04:00+00:00',
      targets: [buildTarget({ waveNumber: 0, isCanary: true, status: 'queued' })],
      waves: [
        {
          waveNumber: 0,
          status: 'running',
          targetCount: 1,
          approvedByGitHubUserId: '1001',
          approvedAt: '2026-08-29T12:05:00+00:00',
          completedAt: null,
        },
      ],
    });
    const paused = buildCampaign({
      ...running,
      status: 'paused',
      revision: 4,
      pausedAt: '2026-08-29T12:06:00+00:00',
    });
    installFetch({
      campaign: running,
      mutatedCampaign: paused,
      onPost: (request) => postedRequests.push(request),
    });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns/${campaignId}`);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Pause' }));
    const dialog = screen.getByRole('alertdialog', {
      name: 'Pause future campaign dispatch?',
    });
    const requestKey = within(dialog).getByText('Request key').parentElement;
    if (requestKey === null) throw new Error('Expected the pause request key.');
    expect(within(requestKey).getByText(/^[0-9a-f-]{36}$/u)).toBeInTheDocument();
    expect(dialog).toHaveTextContent(
      'Does not withdraw or stop an existing profile-image command.',
    );
    expect(within(dialog).getByRole('button', { name: 'Pause' })).toBeDisabled();
    await user.click(
      within(dialog).getByRole('checkbox', {
        name: 'I understand existing profile commands continue to terminal evidence.',
      }),
    );
    await user.click(within(dialog).getByRole('button', { name: 'Pause' }));

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    const request = postedRequests[0];
    if (!request) throw new Error('Expected a campaign pause request.');
    expect(new URL(request.url).pathname).toBe(
      `/api/tenants/${tenantId}/images/campaigns/${campaignId}/pause`,
    );
    expect(await request.clone().json()).toEqual({
      expectedRevision: 3,
      expectedTargetSetHash: 'f'.repeat(64),
    });
    expect(await screen.findByRole('button', { name: 'Resume' })).toBeVisible();
  });

  it('shows each exact rollback candidate ID before wave approval', async () => {
    const rollbackCandidateId = '70300000-0000-4000-8000-000000000099';
    const rollback = buildCampaign({
      kind: 'rollback',
      sourceCampaignId: '71000000-0000-4000-8000-000000000099',
      candidate: null,
      status: 'awaiting-approval',
      revision: 1,
      waveSize: 10,
      configuredByGitHubUserId: '1001',
      configuredAt: '2026-08-29T12:04:00+00:00',
      targets: [
        buildTarget({
          candidate: {
            candidateId: rollbackCandidateId,
            recipeId: 'ubuntu-runner',
            targetDigest: `sha256:${'9'.repeat(64)}`,
            targetPlatform: 'linux/amd64',
          },
          waveNumber: 0,
          isCanary: true,
        }),
      ],
      waves: [
        {
          waveNumber: 0,
          status: 'pending',
          targetCount: 1,
          approvedByGitHubUserId: null,
          approvedAt: null,
          completedAt: null,
        },
      ],
    });
    installFetch({ campaign: rollback });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns/${campaignId}`);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Approve wave' }));

    expect(
      within(screen.getByRole('alertdialog', { name: 'Approve canary?' })).getByText(
        new RegExp(rollbackCandidateId, 'u'),
      ),
    ).toBeInTheDocument();
  });

  it('opens the distinct rollback draft returned by the server', async () => {
    const postedRequests: Request[] = [];
    const source = buildCampaign({
      status: 'complete',
      completedAt: '2026-08-29T12:10:00+00:00',
      targets: [
        buildTarget({
          status: 'complete',
          completedAt: '2026-08-29T12:10:00+00:00',
          previousCandidateId: '70300000-0000-4000-8000-000000000099',
          previousRecipeId: 'ubuntu-runner',
          previousImageReference: 'ghcr.io/example/runner:previous',
          previousImageDigest: `sha256:${'9'.repeat(64)}`,
          previousWorkerRevision: '8'.repeat(64),
        }),
      ],
    });
    const rollback = buildCampaign({
      campaignId: rollbackCampaignId,
      kind: 'rollback',
      sourceCampaignId: campaignId,
      candidate: null,
      targets: [
        buildTarget({
          candidate: {
            candidateId: '70300000-0000-4000-8000-000000000099',
            recipeId: 'ubuntu-runner',
            targetDigest: `sha256:${'9'.repeat(64)}`,
            targetPlatform: 'linux/amd64',
          },
        }),
      ],
    });
    installFetch({
      campaign: source,
      rollbackCampaign: rollback,
      onPost: (request) => postedRequests.push(request),
    });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns/${campaignId}`);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Create rollback draft' }));
    const dialog = screen.getByRole('alertdialog', {
      name: 'Create a separate rollback campaign draft?',
    });
    await user.click(
      within(dialog).getByRole('checkbox', {
        name: 'I understand this creates reviewable rollback work and does not execute it.',
      }),
    );
    await user.click(within(dialog).getByRole('button', { name: 'Create rollback draft' }));

    await waitFor(() => expect(postedRequests).toHaveLength(1));
    const request = postedRequests[0];
    if (!request) throw new Error('Expected a rollback campaign request.');
    expect(new URL(request.url).pathname).toBe(
      `/api/tenants/${tenantId}/images/campaigns/${campaignId}/rollback`,
    );
    expect(await screen.findByRole('heading', { name: 'Rollback campaign' })).toBeVisible();
    expect(screen.getByTestId('location')).toHaveTextContent(
      `/tenants/${tenantId}/images/campaigns/${rollbackCampaignId}`,
    );
    expect(screen.getByText('Source campaign').parentElement).toHaveTextContent(campaignId);
  });

  it('retains a missing campaign as read-only evidence after a successful load', async () => {
    vi.useFakeTimers();
    const campaign = buildCampaign({
      status: 'awaiting-approval',
      revision: 1,
      waveSize: 10,
      configuredByGitHubUserId: '1001',
      configuredAt: '2026-08-29T12:04:00+00:00',
      targets: [buildTarget({ waveNumber: 0, isCanary: true })],
      waves: [
        {
          waveNumber: 0,
          status: 'pending',
          targetCount: 1,
          approvedByGitHubUserId: null,
          approvedAt: null,
          completedAt: null,
        },
      ],
    });
    const fetchControl = installFetch({ campaign });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns/${campaignId}`);

    await act(() => vi.advanceTimersByTimeAsync(0));
    expect(screen.getByRole('heading', { name: 'ubuntu-runner campaign' })).toBeVisible();
    fetchControl.setDetailMissing(true);
    await act(() => vi.advanceTimersByTimeAsync(8_000));

    expect(
      screen.getByText(/Showing retained campaign evidence because the selected campaign/),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: 'Pause' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Approve wave' })).toBeDisabled();
  });

  it('distinguishes unavailable candidate evidence from an empty successful result', async () => {
    installFetch({ campaign: buildCampaign() });
    renderPage('administrator', `/tenants/${tenantId}/images/campaigns`, {
      data: null,
      error: 'Candidate API unavailable.',
    });

    expect(await screen.findByText(/Ready candidate evidence is unavailable/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Freeze campaign plan' })).not.toBeInTheDocument();
  });
});

function renderPage(
  role: 'viewer' | 'administrator',
  path: string,
  overrides: Partial<ImageWorkspaceContext> = {},
) {
  const context: ImageWorkspaceContext = {
    tenantId,
    antiforgeryToken: 'antiforgery-token',
    canAdminister: role === 'administrator',
    data: {
      registrations: [],
      registrationsTruncated: false,
      requests: [],
      requestsTruncated: false,
      candidates: [buildCandidate()],
      candidatesTruncated: false,
    },
    error: null,
    isLoading: false,
    refresh: vi.fn(),
    ...overrides,
  };
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/tenants/:tenantId/images" element={<Outlet context={context} />}>
          <Route path="campaigns" element={<ImageCampaignsPage />} />
          <Route path="campaigns/:campaignId" element={<ImageCampaignsPage />} />
        </Route>
      </Routes>
      <LocationProbe />
    </MemoryRouter>,
  );
}

function installFetch({
  campaign,
  configuredCampaign = campaign,
  approvedCampaign = campaign,
  mutatedCampaign = campaign,
  rollbackCampaign,
  onPost,
}: {
  readonly campaign: ImageCampaign;
  readonly configuredCampaign?: ImageCampaign;
  readonly approvedCampaign?: ImageCampaign;
  readonly mutatedCampaign?: ImageCampaign;
  readonly rollbackCampaign?: ImageCampaign;
  readonly onPost?: (request: Request) => void;
}) {
  let detailMissing = false;
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const request = input instanceof Request ? input : new Request(input, init);
    const path = new URL(request.url).pathname;
    if (request.method === 'GET' && path.endsWith(`/images/campaigns/${campaignId}`)) {
      if (detailMissing) {
        return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
      }
      return jsonResponse(campaign);
    }
    if (
      request.method === 'GET' &&
      rollbackCampaign &&
      path.endsWith(`/images/campaigns/${rollbackCampaign.campaignId}`)
    ) {
      return jsonResponse(rollbackCampaign);
    }
    if (request.method === 'GET' && path.endsWith('/images/campaigns')) {
      return jsonResponse({ campaigns: [buildSummary(campaign)], truncated: false });
    }
    if (request.method === 'POST') {
      onPost?.(request);
      if (path.endsWith('/configure')) return jsonResponse(configuredCampaign);
      if (path.endsWith('/approve')) return jsonResponse(approvedCampaign);
      if (path.endsWith('/pause') || path.endsWith('/resume') || path.endsWith('/cancel')) {
        return jsonResponse(mutatedCampaign);
      }
      if (path.endsWith('/rollback') && rollbackCampaign) {
        return jsonResponse(rollbackCampaign, 201);
      }
      if (path.endsWith('/images/campaigns')) return jsonResponse(campaign, 201);
    }
    throw new Error(`Unexpected request: ${request.method} ${path}`);
  });
  return {
    setDetailMissing(value: boolean) {
      detailMissing = value;
    },
  };
}

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}</output>;
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
    sourceCommit: 'b'.repeat(40),
    githubRunId: '98765',
    githubRunApiUrl: 'https://api.github.com/repos/example/runner-images/actions/runs/98765',
    githubRunUrl: 'https://github.com/example/runner-images/actions/runs/98765',
    artifactId: '4567',
    artifactName: 'pitcrew-image-candidate',
    artifactDigest: `sha256:${'c'.repeat(64)}`,
    reportHash: 'd'.repeat(64),
    imageReference: 'ghcr.io/example/runner:candidate',
    digest: `sha256:${'e'.repeat(64)}`,
    immutableReference: `ghcr.io/example/runner@sha256:${'e'.repeat(64)}`,
    platform: 'linux/amd64',
    outputMode: 'registry',
    failureCategory: null,
    failureDetail: null,
    createdAt: '2026-08-29T11:59:00+00:00',
    storedAt: '2026-08-29T12:00:00+00:00',
    qualifications: [
      { name: 'image-build', status: 'passed' },
      { name: 'buildkit-digest', status: 'passed' },
      { name: 'registry-digest', status: 'passed' },
      { name: 'oci-manifest', status: 'passed' },
      { name: 'builder-cleanup', status: 'passed' },
    ],
  });
}

function buildTarget(overrides: Partial<ImageCampaignTarget> = {}): ImageCampaignTarget {
  return imageCampaignTargetSchema.parse({
    targetId,
    nodeId: 'a1235ec4-2a15-4f91-a9e0-811152869a51',
    nodeDisplayName: 'Alpha',
    profileId: 'build',
    candidate: {
      candidateId,
      recipeId: 'ubuntu-runner',
      targetDigest: `sha256:${'e'.repeat(64)}`,
      targetPlatform: 'linux/amd64',
    },
    expectedCurrentImageReference: 'ghcr.io/example/runner:current',
    expectedCurrentImageDigest: `sha256:${'1'.repeat(64)}`,
    expectedCurrentLocalImageId: `sha256:${'2'.repeat(64)}`,
    expectedCurrentWorkerRevision: '3'.repeat(64),
    expectedStaticFingerprint: '4'.repeat(64),
    expectedPreservedConfigurationFingerprint: '5'.repeat(64),
    expectedRoutingFingerprint: '6'.repeat(64),
    expectedDesiredGeneration: 4,
    expectedDesiredStateHash: 'a'.repeat(64),
    exclusionCategory: null,
    status: 'eligible',
    waveNumber: null,
    isCanary: false,
    commandId: null,
    failureCategory: null,
    resultMessage: null,
    targetWorkerRevision: null,
    managerConvergenceStatus: null,
    currentWorkers: null,
    staleWorkers: null,
    claimedAt: null,
    startedAt: null,
    completedAt: null,
    previousCandidateId: null,
    previousRecipeId: null,
    previousImageReference: null,
    previousImageDigest: null,
    previousWorkerRevision: null,
    ...overrides,
  });
}

function buildCampaign(overrides: Partial<ImageCampaign> = {}): ImageCampaign {
  return imageCampaignSchema.parse({
    campaignId,
    kind: 'forward',
    sourceCampaignId: null,
    candidate: {
      candidateId,
      recipeId: 'ubuntu-runner',
      targetDigest: `sha256:${'e'.repeat(64)}`,
      targetPlatform: 'linux/amd64',
    },
    targetSetHash: 'f'.repeat(64),
    status: 'draft',
    revision: 0,
    waveSize: null,
    requestedByGitHubUserId: '1001',
    requestedAt: '2026-08-29T12:00:00+00:00',
    configuredByGitHubUserId: null,
    configuredAt: null,
    pausedAt: null,
    cancelledAt: null,
    completedAt: null,
    targets: [
      buildTarget(),
      buildTarget({
        targetId: '71200000-0000-4000-8000-000000000002',
        nodeId: 'b2235ec4-2a15-4f91-a9e0-811152869a52',
        nodeDisplayName: 'Bravo',
        candidate: null,
        exclusionCategory: 'node-offline',
        status: 'excluded',
        expectedCurrentImageReference: null,
        expectedCurrentImageDigest: null,
        expectedCurrentLocalImageId: null,
        expectedCurrentWorkerRevision: null,
        expectedStaticFingerprint: null,
        expectedPreservedConfigurationFingerprint: null,
        expectedRoutingFingerprint: null,
        expectedDesiredGeneration: null,
        expectedDesiredStateHash: null,
      }),
    ],
    waves: [],
    ...overrides,
  });
}

function buildSummary(campaign: ImageCampaign) {
  return imageCampaignSummarySchema.parse({
    campaignId: campaign.campaignId,
    kind: campaign.kind,
    sourceCampaignId: campaign.sourceCampaignId,
    candidate: campaign.candidate,
    targetSetHash: campaign.targetSetHash,
    status: campaign.status,
    revision: campaign.revision,
    waveSize: campaign.waveSize,
    eligibleTargetCount: campaign.targets.filter((target) => target.exclusionCategory === null)
      .length,
    excludedTargetCount: campaign.targets.filter((target) => target.exclusionCategory !== null)
      .length,
    completeTargetCount: campaign.targets.filter((target) => target.status === 'complete').length,
    adverseTargetCount: campaign.targets.filter((target) =>
      ['failed', 'blocked', 'indeterminate'].includes(target.status),
    ).length,
    currentWaveNumber: null,
    nextWaveNumber: campaign.waves.find((wave) => wave.status === 'pending')?.waveNumber ?? null,
    requestedByGitHubUserId: campaign.requestedByGitHubUserId,
    requestedAt: campaign.requestedAt,
    configuredAt: campaign.configuredAt,
    completedAt: campaign.completedAt,
  });
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
