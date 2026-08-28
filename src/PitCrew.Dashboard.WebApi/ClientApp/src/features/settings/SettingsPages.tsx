import { type ReactNode, useMemo, useState } from 'react';
import { Navigate, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { hasMinimumTenantRole, useSession, type TenantRole } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';
import { CopyableId } from '@/core/ui/CopyableId';
import { FormField } from '@/core/ui/FormField';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import type { TaskNavigationItem } from '@/core/ui/TaskNavigation';
import { TaskWorkspace } from '@/core/ui/TaskWorkspace';

import { createEnrollmentCode, type EnrollmentCodeResponse } from './settingsApi';
import { DiagnosticCredentials } from './DiagnosticCredentials';
import { OneTimeValue } from './OneTimeValue';
import { SettingsTask } from './SettingsTask';
import { TenantAdministration } from './TenantAdministration';
import { TenantSettings } from './TenantSettings';

function useCurrentTenant() {
  const { tenantId = '' } = useParams();
  const sessionContext = useSession();
  const tenant = sessionContext.session?.tenants.find(
    (candidate) => candidate.tenantId === tenantId,
  );
  return { tenantId, tenant, ...sessionContext };
}

interface SettingsPageProps {
  readonly children: ReactNode;
}

function SettingsPage({ children }: SettingsPageProps) {
  const { tenantId, tenant } = useCurrentTenant();
  const settingsPath = `/tenants/${encodeURIComponent(tenantId)}/settings`;

  const items = useMemo(() => {
    if (!tenant) return [];
    const navItems: TaskNavigationItem[] = [];
    if (hasMinimumTenantRole(tenant.role, 'owner')) {
      navItems.push(
        {
          label: 'General',
          description: 'Tenant identity and operator-facing name.',
          path: `${settingsPath}/general`,
        },
        {
          label: 'Access',
          description: 'Membership, roles, and tenant ownership.',
          path: `${settingsPath}/access`,
        },
      );
    }
    navItems.push({
      label: 'Enrollment',
      description: 'Issue a one-time connector enrollment code.',
      path: `${settingsPath}/enrollment`,
    });
    if (hasMinimumTenantRole(tenant.role, 'administrator')) {
      navItems.push({
        label: 'Diagnostics',
        description: 'Read-only diagnostic credential lifecycle.',
        path: `${settingsPath}/diagnostics`,
      });
    }
    return navItems;
  }, [settingsPath, tenant]);

  if (!tenant) return null;

  return (
    <section className="grid min-w-0 gap-5">
      <ReadinessSummary
        title="Administration context"
        description="Navigation reflects your current tenant role. Dashboard APIs remain the final authority for every settings action."
        narrowColumns={2}
        status={<StatusBadge status={formatRole(tenant.role)} tone="neutral" />}
        items={[
          {
            label: 'Tenant',
            value: tenant.displayName,
            detail: 'Current operator-facing identity',
          },
          {
            label: 'Stable tenant ID',
            value: <CopyableId value={tenantId} label="tenant ID" />,
            detail: 'Immutable routing and authorization identity',
          },
          {
            label: 'Your authority',
            value: formatRole(tenant.role),
            detail: describeRole(tenant.role),
          },
          {
            label: 'Available tasks',
            value: items.length,
            detail: 'Filtered by browser-visible tenant role',
          },
        ]}
      />
      <TaskWorkspace navigationLabel="Tenant settings" navigationItems={items}>
        {children}
      </TaskWorkspace>
    </section>
  );
}

function formatRole(role: TenantRole): string {
  return role === 'administrator' ? 'Administrator' : role === 'owner' ? 'Owner' : 'Viewer';
}

function describeRole(role: TenantRole): string {
  if (role === 'owner') return 'Identity, membership, enrollment, and diagnostics';
  if (role === 'administrator') return 'Enrollment and diagnostics';
  return 'No settings mutation authority';
}

/** Resolves the shared Settings destination to the first route the current role can access. */
export function SettingsLandingPage() {
  const { tenantId, tenant } = useCurrentTenant();
  if (!tenant) return null;

  const destination = hasMinimumTenantRole(tenant.role, 'owner')
    ? `/tenants/${tenantId}/settings/general`
    : `/tenants/${tenantId}/settings/enrollment`;
  return <Navigate replace to={destination} />;
}

/** Owner-managed general tenant settings route. */
export function GeneralSettingsPage() {
  const { tenantId, tenant, session, refreshSession } = useCurrentTenant();
  if (!tenant || !session) return null;
  return (
    <SettingsPage>
      <TenantSettings
        tenantId={tenantId}
        displayName={tenant.displayName}
        antiforgeryToken={session.antiforgeryToken}
        onRenamed={() => void refreshSession()}
      />
    </SettingsPage>
  );
}

/** Owner-managed tenant membership route. */
export function AccessSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  if (!session) return null;
  return (
    <SettingsPage>
      <TenantAdministration tenantId={tenantId} antiforgeryToken={session.antiforgeryToken} />
    </SettingsPage>
  );
}

/** Administrator-managed connector enrollment route. */
export function EnrollmentSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  const [label, setLabel] = useState('New server');
  const [code, setCode] = useState<EnrollmentCodeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  if (!session) return null;

  const issueCode = async () => {
    setIsBusy(true);
    try {
      setCode(await createEnrollmentCode(tenantId, label.trim(), session.antiforgeryToken));
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Enrollment code could not be created.');
    } finally {
      setIsBusy(false);
    }
  };

  return (
    <SettingsPage>
      <SettingsTask
        title="Issue one-time enrollment code"
        description="Codes expire quickly and are consumed by exactly one enrollment or re-enrollment."
      >
        <div className="grid gap-4">
          <OperationalList label="Connector enrollment settings">
            <OperationalRow
              title="Connector enrollment"
              description="No reusable enrollment value is retained. Every code is short-lived and consumed once."
              status={<StatusBadge status="One-time" tone="caution" />}
            >
              <form
                className="mt-3 flex min-w-0 flex-wrap items-end gap-3"
                onSubmit={(event) => {
                  event.preventDefault();
                  void issueCode();
                }}
              >
                <FormField label="Connector label" hint="A name for this enrolled node.">
                  <input
                    className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm sm:min-w-64"
                    value={label}
                    onChange={(event) => setLabel(event.target.value)}
                    maxLength={128}
                  />
                </FormField>
                <Button type="submit" disabled={isBusy || label.trim().length === 0}>
                  {isBusy ? 'Creating…' : 'Create one-time code'}
                </Button>
              </form>
            </OperationalRow>
          </OperationalList>
          {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
          {code ? (
            <OneTimeValue
              title="Enrollment code ready"
              value={code.code}
              description={`Expires ${formatTime(code.expiresAt)}. Set PitCrew__Connector__EnrollmentCode to this value, start the connector, then remove the consumed variable. The code is not stored in recoverable form.`}
              onClear={() => setCode(null)}
            />
          ) : null}
        </div>
      </SettingsTask>
    </SettingsPage>
  );
}

/** Administrator-managed noninteractive diagnostics route. */
export function DiagnosticsSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  if (!session) return null;
  return (
    <SettingsPage>
      <DiagnosticCredentials tenantId={tenantId} antiforgeryToken={session.antiforgeryToken} />
    </SettingsPage>
  );
}
