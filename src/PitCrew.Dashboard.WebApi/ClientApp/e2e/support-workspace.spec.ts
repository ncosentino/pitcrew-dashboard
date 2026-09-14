import { expect, test } from '@playwright/test';

import { viewports } from '../playwright.config';
import { healthyScenario, tenantId } from './mocks/scenarios';
import {
  buildFleetNode,
  buildFleetResponse,
  buildIncident,
  buildIncidentPage,
  buildProfile,
  nodeIds,
} from './mocks/fixtures';
import {
  buildSupportIdentity,
  buildSupportSession,
  supportNodeIds,
  supportSessionIds,
} from './mocks/support';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import { setUpPage } from './support/session';

const completedSession = buildSupportSession({
  sessionId: supportSessionIds.completed,
  diagnosticMode: 'ConnectorOffline',
  profileId: null,
  status: 'Completed',
  requestedAt: '2026-08-27T14:00:00+00:00',
  expiresAt: '2026-08-27T14:15:00+00:00',
  dispatchedAt: '2026-08-27T14:00:10+00:00',
  result: {
    report: {
      verified: ['connector'],
      unavailable: [],
      hypotheses: [],
    },
    markdown: 'Verified connector evidence.',
    attestation: {
      nodeSigningPublicKeySpki: 'synthetic-spki',
      payloadBase64Url: 'synthetic-payload',
      signatureBase64Url: 'synthetic-signature',
      signatureAlgorithm: 'ES256-P1363',
    },
  },
});

const scenario = {
  ...healthyScenario(),
  supportIdentities: [
    buildSupportIdentity({
      displayName: 'Primary build host — ビルド診断 🚀 مشغل الدعم',
    }),
    buildSupportIdentity({
      nodeId: supportNodeIds.revoked,
      displayName: 'Retired build host',
      status: 'Revoked',
      revokedAt: '2026-08-26T12:00:00+00:00',
      lastPollAt: null,
      lastResultAt: null,
    }),
  ],
  supportSessions: [
    completedSession,
    buildSupportSession({
      profileId: `profile-${'x'.repeat(120)}`,
    }),
  ],
};

test.describe('support workspace', () => {
  test('leads with readiness and routes routine work by task', async ({ page }) => {
    await page.setViewportSize(viewports.desktop);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/support`);
    await page.waitForLoadState('networkidle');

    await expect(
      page.getByRole('heading', { level: 1, name: 'Support diagnostics' }),
    ).toBeVisible();
    await expect(page.getByRole('region', { name: 'Support readiness' })).toContainText(
      'Eligible for new diagnostic sessions',
    );
    const runDiagnostic = page.getByRole('link', { name: /Run diagnostic/ }).first();
    await runDiagnostic.focus();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(`/tenants/${tenantId}/support/run`);
    await expect(page.getByRole('combobox', { name: 'Problem to investigate' })).toHaveValue(
      'ConnectorOffline',
    );
    await expect(
      page.getByText(/Dashboard is not receiving normal connector reports/i),
    ).toBeVisible();
  });

  test('keeps sessions scannable and one detail investigation dominant', async ({
    page,
  }, testInfo) => {
    await page.setViewportSize(viewports.desktop);
    await setUpPage(page, scenario, 'dark');
    await page.goto(`/tenants/${tenantId}/support/sessions/${completedSession.sessionId}`);
    await page.waitForLoadState('networkidle');

    const sessionList = page.getByRole('list', { name: 'Support sessions' });
    await expect(sessionList.getByRole('listitem').first()).toContainText('Host pressure');
    await expect(sessionList.getByRole('link', { name: 'Selected' })).toHaveAttribute(
      'href',
      `/tenants/${tenantId}/support/sessions/${completedSession.sessionId}`,
    );
    const detail = page.getByRole('region', { name: 'Connector reporting unavailable' });
    await expect(detail).toContainText('Verified connector evidence.');
    await expect(detail.getByText('Structured report and attestation')).toBeVisible();
    const axeResult = await runAxeCheck(page, testInfo, 'support-populated-session-detail');
    expect(axeResult.unexpected).toHaveLength(0);
  });

  test('contains task navigation and long evidence on narrow and zoomed layouts', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 640, height: 900 });
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/support/sessions/${completedSession.sessionId}`);
    await page.waitForLoadState('networkidle');

    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
  });

  test('keeps enrollment and revoked identity history progressively disclosed', async ({
    page,
  }) => {
    await page.setViewportSize(viewports.mobile);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/support/nodes`);
    await page.waitForLoadState('networkidle');

    await expect(page.getByRole('group', { name: 'Create node enrollment' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Enroll support node' }).click();
    await expect(page.getByRole('group', { name: 'Create node enrollment' })).toBeVisible();
    await expect(page.getByText('Revoked history (1)')).toBeVisible();
    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
  });

  test('connects a scoped incident to exact support evidence and returns without implying remediation', async ({
    page,
  }) => {
    const incident = buildIncident({
      incidentId: 'f9000000-0000-4000-8000-000000000001',
      nodeId: nodeIds.alpha,
      profileId: 'build',
      title: 'Build capacity unavailable',
    });
    const node = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Alpha build host',
      isOnline: true,
      profiles: [buildProfile('build')],
    });
    if (completedSession.result === null) {
      throw new Error('The completed support fixture requires a verified result.');
    }
    const exactResult = {
      ...completedSession,
      sessionId: '20000000-0000-4000-8000-000000000099',
      incidentId: incident.incidentId,
      profileId: 'build',
      diagnosticMode: 'CapacityMismatch',
      result: {
        ...completedSession.result,
        markdown: 'Verified retained capacity evidence; remediation remains unproven.',
      },
    };
    const connectedScenario = {
      ...scenario,
      fleet: buildFleetResponse([node], [incident]),
      incidents: buildIncidentPage([incident]),
      supportSessions: [exactResult],
    };
    const returnTo = `/tenants/${tenantId}/incidents?view=active&incident=${incident.incidentId}`;

    await page.setViewportSize(viewports.desktop);
    await setUpPage(page, connectedScenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page
      .getByTestId(`fleet-node-${node.nodeId}`)
      .getByRole('link', { name: 'Review 1 open incident record' })
      .click();
    await expect(page).toHaveURL(
      `/tenants/${tenantId}/incidents?view=active&nodeId=${node.nodeId}`,
    );
    await page.getByRole('link', { name: 'Request support diagnostics' }).click();
    await expect(page.getByText(/Selected incident context is preserved/i)).toBeVisible();
    await page.getByRole('combobox', { name: 'Support node' }).selectOption(supportNodeIds.active);
    await page.getByRole('button', { name: 'Request read-only diagnostics' }).click();
    const exactPath = `/tenants/${tenantId}/support/sessions/${exactResult.sessionId}?incidentId=${incident.incidentId}&returnTo=${encodeURIComponent(returnTo)}`;
    await expect(page).toHaveURL(exactPath);
    await expect(page.getByText(/remediation remains unproven/i)).toBeVisible();
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.goBack();
    await expect(page).toHaveURL(exactPath);
    await expect(page.getByRole('link', { name: 'Return to incident' })).toHaveAttribute(
      'href',
      returnTo,
    );
  });
});
