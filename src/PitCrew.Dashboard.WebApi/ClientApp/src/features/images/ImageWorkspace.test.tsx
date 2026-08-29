import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider, type TenantRole } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

import { imagesManifest } from './manifest';
import type { ImageBuildRequest, ImageCandidate, ImageRecipeRegistration } from './imagesApi';

const registrationId = '10000000-0000-4000-8000-000000000001';
const readyRequestId = '20000000-0000-4000-8000-000000000002';
const blockedRequestId = '30000000-0000-4000-8000-000000000003';
const candidateId = '40000000-0000-4000-8000-000000000004';
const sourceCommit = 'a'.repeat(40);
const observedAt = '2026-08-28T12:00:00+00:00';

function session(role: TenantRole) {
  return {
    user: {
      githubUserId: '1001',
      githubLogin: 'operator',
      displayName: 'Operator',
      avatarUrl: null,
    },
    isSystemAdministrator: false,
    tenants: [{ tenantId: 'local', displayName: 'Local fleet', role }],
    antiforgeryToken: 'test-antiforgery-token',
  };
}

function registration(overrides: Partial<ImageRecipeRegistration> = {}): ImageRecipeRegistration {
  return {
    registrationId,
    version: 1,
    githubInstallationId: '101',
    githubRepositoryId: '202',
    githubWorkflowId: '303',
    repositoryOwner: 'example',
    repositoryName: 'runner-images',
    workflowPath: '.github/workflows/build-image.yml',
    workflowBlobSha: 'b'.repeat(40),
    dispatchRef: 'refs/heads/main',
    recipeId: 'ubuntu-runner',
    candidateSchemaVersion: 1,
    allowedSourceRefs: ['refs/heads/main'],
    inputs: [],
    createdByGitHubUserId: '1001',
    createdAt: observedAt,
    disabledByGitHubUserId: null,
    disabledAt: null,
    ...overrides,
  };
}

function buildRequest(overrides: Partial<ImageBuildRequest> = {}): ImageBuildRequest {
  return {
    requestId: readyRequestId,
    registrationId,
    registrationVersion: 1,
    recipeId: 'ubuntu-runner',
    sourceRepository: 'example/runner-images',
    sourceRef: 'refs/heads/main',
    sourceCommit,
    status: 'ready',
    githubRunId: '98765',
    githubRunApiUrl: 'https://api.github.com/repos/example/runner-images/actions/runs/98765',
    githubRunHtmlUrl: 'https://github.com/example/runner-images/actions/runs/98765',
    terminalCategory: null,
    terminalDetail: null,
    requestedAt: observedAt,
    updatedAt: observedAt,
    ...overrides,
  };
}

function candidate(overrides: Partial<ImageCandidate> = {}): ImageCandidate {
  return {
    candidateId,
    requestId: readyRequestId,
    registrationId,
    registrationVersion: 1,
    outcome: 'ready',
    recipeId: 'ubuntu-runner',
    sourceRepository: 'example/runner-images',
    sourceCommit,
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
    createdAt: observedAt,
    storedAt: observedAt,
    qualifications: [
      { name: 'image-build', status: 'passed' },
      { name: 'buildkit-digest', status: 'passed' },
      { name: 'registry-digest', status: 'passed' },
      { name: 'oci-manifest', status: 'passed' },
      { name: 'builder-cleanup', status: 'passed' },
    ],
    ...overrides,
  };
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderImages(
  path: string,
  role: TenantRole = 'owner',
  overrides: {
    readonly registrations?: ImageRecipeRegistration[];
    readonly requests?: ImageBuildRequest[];
    readonly candidates?: ImageCandidate[];
  } = {},
) {
  const registrations = overrides.registrations ?? [registration()];
  const requests = overrides.requests ?? [
    buildRequest({
      requestId: blockedRequestId,
      status: 'blocked',
      terminalCategory: 'artifact-missing',
      terminalDetail: 'The exact run did not publish the required candidate artifact.',
      githubRunId: '98766',
    }),
    buildRequest(),
  ];
  const candidates = overrides.candidates ?? [candidate()];
  const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = String(input);
    const method = init?.method ?? 'GET';
    if (url.endsWith('/api/session')) return jsonResponse(session(role));
    if (url.includes('/fleet/v1/incidents')) return jsonResponse([]);
    if (url.includes('/images/v1/recipes/registrations')) {
      if (method === 'POST' && url.endsWith('/disable')) {
        return jsonResponse({ ...registrations[0], disabledAt: observedAt });
      }
      if (method === 'POST') return jsonResponse(registrations[0], 201);
      return jsonResponse({ registrations, truncated: false });
    }
    if (url.includes('/images/requests')) {
      if (method === 'POST') {
        const body = JSON.parse(String(init?.body)) as { requestId: string };
        return jsonResponse(buildRequest({ requestId: body.requestId, status: 'requested' }), 202);
      }
      return jsonResponse({ requests, truncated: false });
    }
    if (url.includes('/images/candidates/')) return jsonResponse(candidates[0]);
    if (url.includes('/images/candidates')) {
      return jsonResponse({ candidates, truncated: false });
    }
    return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
  });
  const router = createTestRouter(features, [path]);
  render(
    <SessionProvider>
      <RouterProvider router={router} />
    </SessionProvider>,
  );
  return { fetchMock, router };
}

describe('runner image workspace', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('registers one viewer route group and stable task presentations', () => {
    expect(imagesManifest.navigation).toEqual([
      {
        label: 'Runner images',
        description: 'Candidate builds and qualification evidence',
        path: '/tenants/:tenantId/images',
        group: 'operate',
        order: 20,
        icon: 'images',
        activePathPatterns: ['/tenants/:tenantId/images', '/tenants/:tenantId/images/*'],
      },
    ]);
    expect(imagesManifest.routes).toHaveLength(1);
    expect(imagesManifest.routePresentations).toHaveLength(3);
  });

  it('leads with blocked work and canonicalizes one focused candidate detail', async () => {
    const { router } = renderImages('/tenants/local/images/candidates');

    expect(await screen.findByRole('heading', { name: 'Image readiness' })).toBeInTheDocument();
    expect(screen.getByText('Needs attention')).toBeInTheDocument();
    const requestList = await screen.findByRole('list', { name: 'Image build requests' });
    const rows = within(requestList).getAllByRole('listitem');
    expect(rows[0]).toHaveTextContent('artifact-missing');
    await waitFor(() =>
      expect(new URLSearchParams(router.state.location.search).get('request')).toBe(
        blockedRequestId,
      ),
    );
    expect(
      screen.getByRole('heading', {
        level: 2,
        name: `ubuntu-runner · ${sourceCommit.slice(0, 12)}`,
      }),
    ).toBeInTheDocument();

    const readyRow = rows.find((row) => row.textContent?.includes('98765'));
    if (!readyRow) throw new Error('Expected the ready image request row.');
    const user = userEvent.setup();
    await user.click(within(readyRow).getByRole('link', { name: 'Inspect' }));

    await waitFor(() =>
      expect(new URLSearchParams(router.state.location.search).get('request')).toBe(readyRequestId),
    );
    expect(screen.getByText('Immutable candidate evidence')).toBeInTheDocument();
    expect(screen.getByText('Qualification evidence')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open exact GitHub run' })).toHaveAttribute(
      'href',
      'https://github.com/example/runner-images/actions/runs/98765',
    );
  });

  it('does not substitute another build when a deep-linked request is missing', async () => {
    renderImages('/tenants/local/images/candidates?request=90000000-0000-4000-8000-000000000009');

    expect(await screen.findByText(/requested build record is not present/i)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Selected image build evidence' })).toBeNull();
    expect(
      within(screen.getByRole('list', { name: 'Image build requests' })).getAllByRole('listitem')
        .length,
    ).toBeGreaterThan(0);
  });

  it('keeps viewer access read-only', async () => {
    renderImages('/tenants/local/images/candidates', 'viewer');

    expect(await screen.findByText(/Viewer access is read-only/)).toBeInTheDocument();
    expect(screen.queryByText('Request a candidate build')).not.toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Primary navigation' })).getByRole('link', {
        name: 'Runner images',
      }),
    ).toBeInTheDocument();
  });

  it('confirms and submits one exact build request with its stable idempotency key', async () => {
    const { fetchMock } = renderImages('/tenants/local/images/candidates');
    const user = userEvent.setup();

    await user.click(await screen.findByText('Request a candidate build'));
    await user.type(screen.getByLabelText('Exact source commit'), 'f'.repeat(40));
    await user.click(screen.getByRole('button', { name: 'Review build request' }));
    await user.click(
      screen.getByRole('checkbox', {
        name: 'I verified the exact source commit and reviewed recipe authority.',
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Request candidate build' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input, init]) => {
          if (!String(input).endsWith('/api/tenants/local/images/requests')) return false;
          if (init?.method !== 'POST') return false;
          const body = JSON.parse(String(init.body)) as {
            requestId: string;
            sourceCommit: string;
          };
          return body.requestId.length === 36 && body.sourceCommit === 'f'.repeat(40);
        }),
      ).toBe(true),
    );
  });

  it('shows recipe authority and confirms disablement without deleting evidence', async () => {
    const { fetchMock } = renderImages('/tenants/local/images/recipes');
    const user = userEvent.setup();

    expect(await screen.findByText('Frozen workflow authority')).toBeInTheDocument();
    expect(screen.getByText('.github/workflows/build-image.yml')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Disable registration' }));
    const dialog = screen.getByRole('alertdialog', { name: 'Disable ubuntu-runner?' });
    expect(within(dialog).getByText(/Preserves prior requests, candidates/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Disable registration' }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([input, init]) =>
            String(input).endsWith(`/registrations/${registrationId}/disable`) &&
            init?.method === 'POST',
        ),
      ).toBe(true),
    );
  });
});
