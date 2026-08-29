import { test, expect, type Page, type TestInfo } from '@playwright/test';

import { viewports } from '../playwright.config';
import {
  buildImageBuildRequest,
  buildImageCandidate,
  buildImageRecipeRegistration,
  buildProfileImageRolloutCommand,
  buildProfileImageRolloutControl,
  buildSession,
  buildTenantAccess,
} from './mocks/fixtures';
import { healthyScenario, nodeIds, tenantId } from './mocks/scenarios';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import { setUpPage } from './support/session';

async function expectImageSurfaceHealth(
  page: Page,
  testInfo: TestInfo,
  name: string,
): Promise<void> {
  await page.waitForLoadState('networkidle');
  expect((await measureDocumentOverflow(page)).overflowPx, `document overflow on ${name}`).toBe(0);
  const axeResult = await runAxeCheck(page, testInfo, `images-${name}`);
  expect(
    axeResult.unexpected,
    `unexpected axe violations on ${name}: ${axeResult.unexpected.map((item) => item.id).join(', ')}`,
  ).toHaveLength(0);
}

test('candidate workspace leads with immutable qualification evidence', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.desktop);
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/images/candidates`);

  await expect(page.getByRole('heading', { name: 'Image readiness' })).toBeVisible();
  await expect(page.getByText('Candidates ready')).toBeVisible();
  await expect(
    page.getByRole('heading', { level: 2, name: /ubuntu-runner · b{12}/ }),
  ).toBeVisible();
  await expect(page.getByText('Immutable candidate evidence')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Qualification evidence' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open exact GitHub run' })).toHaveAttribute(
    'href',
    'https://github.com/example/runner-images/actions/runs/98765',
  );
  await expect(
    page
      .getByRole('navigation', { name: 'Primary navigation' })
      .getByRole('link', { name: 'Runner images' }),
  ).toHaveAttribute('aria-current', 'page');

  await expectImageSurfaceHealth(page, testInfo, 'candidate-ready-desktop');
  await testInfo.attach('screenshot-image-candidate-ready-desktop', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('blocked requests lead active and ready history without substitution', async ({ page }) => {
  const ready = buildImageBuildRequest();
  const blocked = buildImageBuildRequest({
    requestId: '70400000-0000-4000-8000-000000000004',
    githubRunId: '98766',
    status: 'blocked',
    terminalCategory: 'artifact-identity-mismatch',
    terminalDetail: 'The candidate artifact did not match the exact stored request identity.',
    updatedAt: '2026-08-28T12:20:00+00:00',
  });
  const building = buildImageBuildRequest({
    requestId: '70500000-0000-4000-8000-000000000005',
    githubRunId: '98767',
    status: 'building',
    updatedAt: '2026-08-28T12:25:00+00:00',
  });
  const scenario = {
    ...healthyScenario(),
    imageBuildRequests: [ready, building, blocked],
  };
  await setUpPage(page, scenario, 'dark');
  await page.goto(`/tenants/${tenantId}/images/candidates`);

  const list = page.getByRole('list', { name: 'Image build requests' });
  await expect(list.getByRole('listitem').first()).toContainText('artifact-identity-mismatch');
  await expect(page.getByRole('region', { name: 'Image readiness' })).toContainText(
    'Needs attention',
  );

  await page.goto(
    `/tenants/${tenantId}/images/candidates?request=79900000-0000-4000-8000-000000000099`,
  );
  await expect(page.getByText(/requested build record is not present/i)).toBeVisible();
  await expect(page.getByRole('region', { name: 'Selected image build evidence' })).toHaveCount(0);
  await expect(list.getByRole('listitem')).toHaveCount(3);
});

test('candidate and recipe evidence stays contained on a narrow screen', async ({
  page,
}, testInfo) => {
  const longRecipe = `runner-${'r'.repeat(54)}`;
  const longRef = `refs/heads/release-${'s'.repeat(180)}`;
  const registration = buildImageRecipeRegistration({
    recipeId: longRecipe,
    allowedSourceRefs: [longRef],
  });
  const request = buildImageBuildRequest({
    recipeId: longRecipe,
    sourceRef: longRef,
    sourceRepository: `example/${'repository'.repeat(20)}`,
  });
  const candidate = buildImageCandidate({
    recipeId: longRecipe,
    sourceRepository: request.sourceRepository,
    imageReference: `ghcr.io/example/${'runner-image'.repeat(30)}:candidate`,
    immutableReference: `ghcr.io/example/${'runner-image'.repeat(30)}@sha256:${'e'.repeat(64)}`,
  });
  const scenario = {
    ...healthyScenario(),
    session: buildSession('owner', {
      tenants: [
        buildTenantAccess('owner', {
          displayName: `Release engineering ${'operations '.repeat(8)}`,
        }),
      ],
    }),
    imageRecipeRegistrations: [registration],
    imageBuildRequests: [request],
    imageCandidates: [candidate],
  };
  await page.setViewportSize(viewports.narrow);
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/candidates`);

  await expect(page.getByRole('region', { name: 'Selected image build evidence' })).toBeVisible();
  await expectImageSurfaceHealth(page, testInfo, 'candidate-long-narrow');
  await testInfo.attach('screenshot-image-candidate-long-narrow', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('bounded large request inventory remains explicit and contained', async ({ page }) => {
  const requests = Array.from({ length: 100 }, (_, index) =>
    buildImageBuildRequest({
      requestId: `70600000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
      githubRunId: String(99000 + index),
      status: 'building',
      sourceCommit: index.toString(16).padStart(40, '0'),
      updatedAt: `2026-08-28T12:${String(index % 60).padStart(2, '0')}:00+00:00`,
    }),
  );
  const scenario = {
    ...healthyScenario(),
    imageBuildRequests: requests,
    imageBuildRequestsTruncated: true,
    imageCandidates: [],
  };
  await page.setViewportSize(viewports.mobile);
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/candidates`);

  await expect(page.getByText(/bounded view shows the newest 100 records/i)).toBeVisible();
  await expect(
    page.getByRole('list', { name: 'Image build requests' }).getByRole('listitem'),
  ).toHaveCount(100);
  expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
});

test('recipe administration is role-aware and confirmation names prohibited effects', async ({
  page,
}) => {
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/images/recipes`);

  await expect(page.getByText('Frozen workflow authority')).toBeVisible();
  await page.getByRole('button', { name: 'Disable registration' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Disable ubuntu-runner?' });
  await expect(dialog).toContainText('Does not cancel active GitHub workflow runs.');
  await expect(dialog.getByRole('button', { name: 'Disable registration' })).toBeDisabled();
  await dialog
    .getByRole('checkbox', {
      name: 'I verified the exact registration version and workflow blob to disable.',
    })
    .check();
  await expect(dialog.getByRole('button', { name: 'Disable registration' })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Cancel' }).click();

  const viewerScenario = {
    ...healthyScenario(),
    session: buildSession('viewer'),
  };
  await setUpPage(page, viewerScenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/recipes`);
  await expect(page.getByText(/Viewer access is read-only/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Disable registration' })).toHaveCount(0);
  await expect(page.getByText('Register a trusted image recipe')).toHaveCount(0);
});

test('profile changeover keeps candidate, fences, and prohibited effects together', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.desktop);
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/image`);

  await expect(page.getByRole('heading', { name: 'Profile image rollout' })).toBeVisible();
  await expect(page.getByText('Current profile image')).toBeVisible();
  await expect(page.getByText('Selected candidate')).toBeVisible();
  await expect(page.getByText('Preserved operating contract')).toBeVisible();
  await page.getByRole('button', { name: 'Roll out image' }).click();

  const dialog = page.getByRole('alertdialog', { name: 'Roll out ubuntu-runner?' });
  await expect(dialog).toContainText('No automatic rollback or fleet campaign.');
  await expect(dialog.getByText('Static profile')).toBeVisible();
  await expect(dialog.getByText('Routing')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Roll out image' })).toBeDisabled();

  const requestPromise = page.waitForRequest(
    (request) =>
      request.method() === 'POST' &&
      request.url().endsWith(`/api/tenants/${tenantId}/images/profile-rollouts`),
  );
  await dialog
    .getByRole('checkbox', {
      name: 'I verified the exact candidate, profile, and current fences for this image-only change.',
    })
    .check();
  await dialog.getByRole('button', { name: 'Roll out image' }).click();
  const request = await requestPromise;

  expect(request.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/u);
  expect(request.headers()['x-pitcrew-antiforgery']).toBe('e2e-antiforgery-token');
  expect(request.postDataJSON()).toMatchObject({
    nodeId: nodeIds.alpha,
    profileId: 'build',
    candidateId: '70300000-0000-4000-8000-000000000003',
    expectedDesiredGeneration: 4,
  });
  await expect(page.getByRole('status')).toContainText('is queued');

  await expectImageSurfaceHealth(page, testInfo, 'profile-changeover-desktop');
  await testInfo.attach('screenshot-profile-image-changeover-desktop', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('profile rollout keeps viewer and indeterminate state explicit', async ({ page }) => {
  const indeterminate = buildProfileImageRolloutCommand({
    status: 'indeterminate',
    failureCategory: 'unknown',
    completedAt: '2026-08-28T12:30:00+00:00',
    resultMessage: 'The started rollout could not be proved.',
  });
  const scenario = {
    ...healthyScenario(),
    session: buildSession('viewer'),
    profileImageRolloutControl: buildProfileImageRolloutControl({
      latestCommand: indeterminate,
      recentCommands: [indeterminate],
    }),
  };
  await setUpPage(page, scenario, 'dark');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/image`);

  await expect(page.getByText(/Viewer access is read-only/)).toBeVisible();
  await expect(page.getByText('Indeterminate')).toBeVisible();
  await expect(page.getByText(/never executed automatically again/i)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Roll out image' })).toBeDisabled();
});

test('profile changeover contains long candidate evidence on a narrow screen', async ({
  page,
}, testInfo) => {
  const recipeId = `runner-${'r'.repeat(50)}`;
  const candidate = buildImageCandidate({
    recipeId,
    sourceRepository: `example/${'runner-images-'.repeat(15)}release`,
    imageReference: `ghcr.io/example/${'runner-image-'.repeat(20)}candidate:latest`,
    immutableReference: `ghcr.io/example/${'runner-image-'.repeat(20)}candidate@sha256:${'e'.repeat(64)}`,
  });
  const scenario = {
    ...healthyScenario(),
    imageCandidates: [candidate],
    profileImageRolloutControl: buildProfileImageRolloutControl({
      allowedRecipeIds: [recipeId],
    }),
  };
  await page.setViewportSize(viewports.narrow);
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/image`);

  await expect(page.getByText(recipeId).first()).toBeVisible();
  await expectImageSurfaceHealth(page, testInfo, 'profile-changeover-long-narrow');
  await testInfo.attach('screenshot-profile-image-changeover-long-narrow', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});
