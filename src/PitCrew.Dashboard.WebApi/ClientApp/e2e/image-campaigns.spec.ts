import { expect, test, type Page, type TestInfo } from '@playwright/test';

import { viewports } from '../playwright.config';
import {
  buildImageCampaign,
  buildImageCampaignSummary,
  buildImageCampaignTarget,
  buildImageCampaignWave,
  buildSession,
} from './mocks/fixtures';
import { healthyScenario, nodeIds, tenantId } from './mocks/scenarios';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import { setUpPage } from './support/session';

async function expectCampaignSurfaceHealth(
  page: Page,
  testInfo: TestInfo,
  name: string,
): Promise<void> {
  await page.waitForLoadState('networkidle');
  expect((await measureDocumentOverflow(page)).overflowPx, `document overflow on ${name}`).toBe(0);
  const axeResult = await runAxeCheck(page, testInfo, `campaign-${name}`);
  expect(
    axeResult.unexpected,
    `unexpected axe violations on ${name}: ${axeResult.unexpected
      .map((item) => item.id)
      .join(', ')}`,
  ).toHaveLength(0);
}

test('campaign draft keeps eligible and excluded targets visible before authority', async ({
  page,
}, testInfo) => {
  const campaign = buildImageCampaign();
  const scenario = {
    ...healthyScenario(),
    imageCampaigns: [buildImageCampaignSummary()],
    imageCampaignDetails: [campaign],
  };
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/campaigns/${campaign.campaignId}`);

  await expect(page.getByRole('heading', { name: 'Plan a frozen campaign' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Freeze canary and waves' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Campaign targets' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Excluded targets' })).toBeVisible();
  await expect(page.getByText('Node offline')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Freeze campaign waves' })).toBeEnabled();
  await page.getByRole('button', { name: 'Freeze campaign waves' }).click();
  const configureDialog = page.getByRole('alertdialog', {
    name: 'Freeze canary and wave assignment?',
  });
  await expect(configureDialog.getByText('Request key', { exact: true })).toBeVisible();
  await expect(configureDialog).toContainText(campaign.targetSetHash);
  await expect(
    configureDialog.getByRole('button', { name: 'Freeze campaign waves' }),
  ).toBeDisabled();
  await configureDialog.getByRole('button', { name: 'Cancel' }).click();
  await page.getByRole('button', { name: 'Cancel future dispatch' }).click();
  const cancelDialog = page.getByRole('alertdialog', {
    name: 'Cancel future campaign dispatch?',
  });
  await expect(cancelDialog.getByText('Request key', { exact: true })).toBeVisible();
  await expect(cancelDialog).toContainText('Existing commands continue.');
  await cancelDialog.getByRole('button', { name: 'Cancel' }).click();

  await expectCampaignSurfaceHealth(page, testInfo, 'draft-desktop');
  await testInfo.attach('screenshot-image-campaign-draft-desktop', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('campaign wave approval keeps target hash and prohibited effects together', async ({
  page,
}) => {
  const campaign = buildImageCampaign({
    status: 'awaiting-approval',
    revision: 1,
    waveSize: 10,
    configuredByGitHubUserId: '1001',
    configuredAt: '2026-08-29T12:04:00+00:00',
    targets: [
      buildImageCampaignTarget({
        waveNumber: 0,
        isCanary: true,
      }),
    ],
    waves: [buildImageCampaignWave()],
  });
  const scenario = {
    ...healthyScenario(),
    imageCampaigns: [
      buildImageCampaignSummary({
        status: 'awaiting-approval',
        revision: 1,
        waveSize: 10,
        eligibleTargetCount: 1,
        excludedTargetCount: 0,
        nextWaveNumber: 0,
      }),
    ],
    imageCampaignDetails: [campaign],
  };
  await setUpPage(page, scenario, 'dark');
  await page.goto(`/tenants/${tenantId}/images/campaigns/${campaign.campaignId}`);

  await page.getByRole('button', { name: 'Approve wave' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Approve canary?' });
  if (!campaign.candidate) throw new Error('Expected forward campaign authority.');
  await expect(dialog).toContainText(campaign.targetSetHash);
  await expect(dialog).toContainText(campaign.candidate.candidateId);
  await expect(dialog).toContainText(campaign.candidate.targetDigest);
  await expect(dialog).toContainText(campaign.candidate.targetPlatform);
  await expect(dialog).toContainText(`${nodeIds.alpha} · build`);
  await expect(dialog).toContainText('No newly discovered target joins this campaign.');
  await expect(dialog).toContainText('No automatic later-wave approval');
  await expect(dialog.getByText('Request key', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Approve wave' })).toBeDisabled();
  const requestPromise = page.waitForRequest(
    (request) =>
      request.method() === 'POST' &&
      request
        .url()
        .endsWith(
          `/api/tenants/${tenantId}/images/campaigns/${campaign.campaignId}/waves/0/approve`,
        ),
  );
  await dialog
    .getByRole('checkbox', {
      name: 'I reviewed the exact target set, candidate authority, and current campaign revision.',
    })
    .check();
  await dialog.getByRole('button', { name: 'Approve wave' }).click();
  const request = await requestPromise;

  expect(request.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/u);
  expect(request.headers()['x-pitcrew-antiforgery']).toBe('e2e-antiforgery-token');
  expect(request.postDataJSON()).toEqual({
    expectedRevision: 1,
    expectedTargetSetHash: campaign.targetSetHash,
  });
});

test('mixed campaign progress preserves rolling failed indeterminate and excluded targets', async ({
  page,
}, testInfo) => {
  const targets = [
    buildImageCampaignTarget({
      status: 'applying',
      waveNumber: 0,
      isCanary: true,
      commandId: '72000000-0000-4000-8000-000000000001',
    }),
    buildImageCampaignTarget({
      targetId: '71100000-0000-4000-8000-000000000002',
      nodeId: nodeIds.bravo,
      nodeDisplayName: 'Bravo',
      status: 'rolling',
      waveNumber: 1,
      commandId: '72000000-0000-4000-8000-000000000002',
      currentWorkers: 2,
      staleWorkers: 1,
    }),
    buildImageCampaignTarget({
      targetId: '71100000-0000-4000-8000-000000000003',
      nodeId: nodeIds.charlie,
      nodeDisplayName: 'Charlie',
      status: 'failed',
      waveNumber: 1,
      commandId: '72000000-0000-4000-8000-000000000003',
      failureCategory: 'process-failure',
      resultMessage: 'The local profile operation failed.',
      completedAt: '2026-08-29T12:15:00+00:00',
    }),
    buildImageCampaignTarget({
      targetId: '71100000-0000-4000-8000-000000000004',
      nodeId: nodeIds.alpha,
      nodeDisplayName: 'Alpha',
      profileId: 'deploy',
      status: 'indeterminate',
      waveNumber: 1,
      commandId: '72000000-0000-4000-8000-000000000004',
      failureCategory: 'unknown',
      resultMessage: 'The started operation could not be proved.',
      completedAt: '2026-08-29T12:16:00+00:00',
    }),
    buildImageCampaignTarget({
      targetId: '71100000-0000-4000-8000-000000000005',
      nodeId: nodeIds.bravo,
      nodeDisplayName: 'Bravo',
      profileId: 'deploy',
      candidate: null,
      exclusionCategory: 'recipe-not-allowed',
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
  ];
  const campaign = buildImageCampaign({
    status: 'running',
    revision: 3,
    waveSize: 3,
    configuredByGitHubUserId: '1001',
    configuredAt: '2026-08-29T12:04:00+00:00',
    targets,
    waves: [
      buildImageCampaignWave({
        status: 'running',
        approvedByGitHubUserId: '1001',
        approvedAt: '2026-08-29T12:05:00+00:00',
      }),
      buildImageCampaignWave({
        waveNumber: 1,
        status: 'running',
        targetCount: 3,
        approvedByGitHubUserId: '1001',
        approvedAt: '2026-08-29T12:10:00+00:00',
      }),
    ],
  });
  const scenario = {
    ...healthyScenario(),
    session: buildSession('viewer'),
    imageCampaigns: [
      buildImageCampaignSummary({
        status: 'running',
        revision: 3,
        waveSize: 3,
        eligibleTargetCount: 4,
        excludedTargetCount: 1,
        adverseTargetCount: 2,
        currentWaveNumber: 1,
      }),
    ],
    imageCampaignDetails: [campaign],
  };
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/campaigns/${campaign.campaignId}`);

  await expect(page.getByText('Applying')).toBeVisible();
  await expect(page.getByText('Rolling')).toBeVisible();
  await expect(page.getByText('Failed')).toBeVisible();
  await expect(page.getByText('Indeterminate')).toBeVisible();
  await expect(page.getByText('Recipe not allowed')).toBeVisible();
  await expect(
    page.getByText(
      'Pause and resume change future campaign dispatch only. Existing profile commands continue to terminal evidence.',
    ),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Pause' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Cancel future dispatch' })).toHaveCount(0);

  await expectCampaignSurfaceHealth(page, testInfo, 'mixed-progress-desktop');
});

test('missing campaign deep link never substitutes another retained campaign', async ({ page }) => {
  const campaign = buildImageCampaign();
  const scenario = {
    ...healthyScenario(),
    imageCampaigns: [buildImageCampaignSummary()],
    imageCampaignDetails: [campaign],
  };
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/campaigns/79900000-0000-4000-8000-000000000099`);

  await expect(page.getByText(/selected campaign is not present/i)).toBeVisible();
  await expect(page.getByRole('heading', { name: 'ubuntu-runner campaign' })).toHaveCount(0);
});

test('rollback creation opens the distinct returned draft without executing it', async ({
  page,
}) => {
  const source = buildImageCampaign({
    status: 'complete',
    completedAt: '2026-08-29T12:10:00+00:00',
    targets: [
      buildImageCampaignTarget({
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
  const rollback = buildImageCampaign({
    campaignId: '71000000-0000-4000-8000-000000000002',
    kind: 'rollback',
    sourceCampaignId: source.campaignId,
    candidate: null,
    targets: [
      buildImageCampaignTarget({
        candidate: {
          candidateId: '70300000-0000-4000-8000-000000000099',
          recipeId: 'ubuntu-runner',
          targetDigest: `sha256:${'9'.repeat(64)}`,
          targetPlatform: 'linux/amd64',
        },
      }),
    ],
  });
  const scenario = {
    ...healthyScenario(),
    imageCampaigns: [
      buildImageCampaignSummary({
        status: 'complete',
        completeTargetCount: 1,
        completedAt: '2026-08-29T12:10:00+00:00',
      }),
    ],
    imageCampaignDetails: [source],
    imageRollbackCampaign: rollback,
  };
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/images/campaigns/${source.campaignId}`);

  await page.getByRole('button', { name: 'Create rollback draft' }).click();
  const dialog = page.getByRole('alertdialog', {
    name: 'Create a separate rollback campaign draft?',
  });
  await expect(dialog.getByText('Request key', { exact: true })).toBeVisible();
  await expect(dialog).toContainText('Does not approve a rollback wave');
  await dialog
    .getByRole('checkbox', {
      name: 'I understand this creates reviewable rollback work and does not execute it.',
    })
    .check();
  await dialog.getByRole('button', { name: 'Create rollback draft' }).click();

  await expect(page).toHaveURL(`/tenants/${tenantId}/images/campaigns/${rollback.campaignId}`);
  await expect(page.getByRole('heading', { name: 'Rollback campaign' })).toBeVisible();
  await expect(page.getByText('Source campaign').locator('..')).toContainText(source.campaignId);
  await expect(page.getByText('Draft')).toBeVisible();
});

test('campaign target evidence stays contained on a narrow screen', async ({ page }, testInfo) => {
  const longName = `Builder ${'north-'.repeat(30)}node`;
  const campaign = buildImageCampaign({
    targets: [
      buildImageCampaignTarget({
        nodeDisplayName: longName,
      }),
      buildImageCampaignTarget({
        targetId: '71100000-0000-4000-8000-000000000002',
        nodeId: nodeIds.bravo,
        nodeDisplayName: longName,
        profileId: `profile-${'p'.repeat(24)}`,
        candidate: null,
        exclusionCategory: 'capability-unavailable',
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
  });
  const scenario = {
    ...healthyScenario(),
    imageCampaigns: [buildImageCampaignSummary()],
    imageCampaignDetails: [campaign],
  };
  await page.setViewportSize(viewports.narrow);
  await setUpPage(page, scenario, 'dark');
  await page.goto(`/tenants/${tenantId}/images/campaigns/${campaign.campaignId}`);

  await expect(page.getByText(longName).first()).toBeVisible();
  await expectCampaignSurfaceHealth(page, testInfo, 'long-target-narrow');
  await testInfo.attach('screenshot-image-campaign-long-target-narrow', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});
