import { useCallback, useEffect, useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ApiError } from '@/core/api/httpClient';
import type { DashboardUser, TenantRole } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { EmptyState } from '@/core/ui/EmptyState';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  getAvailableUsers,
  getTenantMembers,
  removeTenantMembership,
  setTenantMembership,
  type TenantMember,
} from './settingsApi';
import { SettingsTask } from './SettingsTask';

/** Props for owner-managed tenant membership administration. */
export interface TenantAdministrationProps {
  readonly tenantId: string;
  readonly antiforgeryToken: string;
}

/** Manages persisted owner-controlled tenant memberships. */
export function TenantAdministration({ tenantId, antiforgeryToken }: TenantAdministrationProps) {
  const [members, setMembers] = useState<readonly TenantMember[]>([]);
  const [availableUsers, setAvailableUsers] = useState<readonly DashboardUser[]>([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [selectedRole, setSelectedRole] = useState<TenantRole>('viewer');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isBusy, setIsBusy] = useState(false);

  const load = useCallback(
    async (signal: AbortSignal) => {
      try {
        const [nextMembers, nextUsers] = await Promise.all([
          getTenantMembers(tenantId, signal),
          getAvailableUsers(tenantId, signal),
        ]);
        setMembers(nextMembers);
        setAvailableUsers(nextUsers);
        setSelectedUserId((current) =>
          nextUsers.some((user) => user.githubUserId === current)
            ? current
            : (nextUsers[0]?.githubUserId ?? ''),
        );
        setError(null);
      } catch (caught) {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        setError(caught instanceof Error ? caught.message : 'Memberships could not be loaded.');
      } finally {
        if (!signal.aborted) setIsLoading(false);
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void load(controller.signal);
    }, 0);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [load]);

  const mutate = async (operation: () => Promise<void>, successMessage: string) => {
    setIsBusy(true);
    setError(null);
    setNotice(null);
    try {
      await operation();
      await load(new AbortController().signal);
      setNotice(successMessage);
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? caught.message
          : caught instanceof Error
            ? caught.message
            : 'Membership could not be changed.',
      );
    } finally {
      setIsBusy(false);
    }
  };
  const selectedUser = availableUsers.find((user) => user.githubUserId === selectedUserId);

  return (
    <SettingsTask
      title="Membership and roles"
      description="Users appear after their first GitHub sign-in. A tenant always retains at least one owner."
    >
      <div className="grid gap-4">
        {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
        {notice ? <StateBanner tone="positive">{notice}</StateBanner> : null}
        {isLoading ? <LoadingState label="Loading tenant memberships…" /> : null}
        {!isLoading && error === null && members.length === 0 ? (
          <EmptyState
            title="No tenant memberships returned"
            description="The membership request succeeded without a visible owner record. Do not infer that the tenant has no owner; refresh or investigate the authoritative API."
          />
        ) : null}
        {!isLoading && members.length > 0 ? (
          <OperationalList label="Tenant members">
            {members.map((member) => (
              <OperationalRow
                key={member.user.githubUserId}
                title={member.user.displayName}
                description={`@${member.user.githubLogin}`}
                status={<StatusBadge status={member.role} tone="neutral" />}
                metadata={
                  <div className="flex min-w-0 flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
                    <span className="[overflow-wrap:anywhere]">
                      GitHub user {member.user.githubUserId}
                    </span>
                    <span>Member since {formatTime(member.createdAt)}</span>
                  </div>
                }
                actions={
                  <ConfirmActionDialog
                    trigger={
                      <Button type="button" variant="outline" size="sm" disabled={isBusy}>
                        Remove
                      </Button>
                    }
                    title={`Remove ${member.user.displayName}?`}
                    description="This will remove the user's access to the tenant."
                    confirmLabel="Remove member"
                    confirmVariant="destructive"
                    details={
                      <ConfirmationSummary
                        identity={[
                          { label: 'User', value: `@${member.user.githubLogin}` },
                          { label: 'Current role', value: member.role },
                        ]}
                        effects={[
                          'The user will lose all access to this tenant.',
                          'They can be re-added later.',
                        ]}
                        prohibitedEffects={[
                          "This does not change the user's access to other tenants.",
                          'This does not delete their Dashboard or GitHub identity.',
                        ]}
                      />
                    }
                    onConfirm={() =>
                      mutate(
                        () =>
                          removeTenantMembership(
                            tenantId,
                            member.user.githubUserId,
                            antiforgeryToken,
                          ),
                        `Removed @${member.user.githubLogin} from this tenant.`,
                      )
                    }
                  />
                }
              />
            ))}
          </OperationalList>
        ) : null}
        <details className="rounded-lg border bg-card">
          <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold outline-none hover:bg-muted/40 focus-visible:ring-2 focus-visible:ring-ring">
            <span>Add a member</span>
            <span className="text-xs font-normal text-muted-foreground">
              {isLoading ? 'Loading' : `${availableUsers.length} available`}
            </span>
          </summary>
          <fieldset className="grid gap-3 border-t p-4 sm:grid-cols-[1fr_auto_auto]">
            <legend className="sr-only">Grant tenant access</legend>
            <FormField label="User" hint="Users appear after their first sign-in.">
              <select
                className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm"
                value={selectedUserId}
                onChange={(event) => setSelectedUserId(event.target.value)}
              >
                {availableUsers.length === 0 ? <option value="">No available users</option> : null}
                {availableUsers.map((user) => (
                  <option key={user.githubUserId} value={user.githubUserId}>
                    @{user.githubLogin}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Role" hint="Viewer, Administrator, or Owner.">
              <select
                className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm"
                value={selectedRole}
                onChange={(event) => setSelectedRole(event.target.value as TenantRole)}
              >
                <option value="viewer">Viewer</option>
                <option value="administrator">Administrator</option>
                <option value="owner">Owner</option>
              </select>
            </FormField>
            <div className="flex items-end">
              <ConfirmActionDialog
                trigger={
                  <Button type="button" disabled={isBusy || selectedUser === undefined}>
                    Add member
                  </Button>
                }
                title={`Add ${selectedUser ? `@${selectedUser.githubLogin}` : 'member'}?`}
                description="This grants access to the current tenant at the selected role."
                confirmLabel="Add member"
                details={
                  <ConfirmationSummary
                    identity={[
                      {
                        label: 'User',
                        value: selectedUser ? `@${selectedUser.githubLogin}` : 'Unavailable',
                      },
                      { label: 'Role', value: selectedRole },
                    ]}
                    effects={['The user can access this tenant with the selected role.']}
                    prohibitedEffects={['This does not grant system-administrator access.']}
                  />
                }
                onConfirm={() =>
                  mutate(
                    () =>
                      setTenantMembership(tenantId, selectedUserId, selectedRole, antiforgeryToken),
                    `Added @${selectedUser?.githubLogin ?? 'user'} as ${selectedRole}.`,
                  )
                }
              />
            </div>
          </fieldset>
        </details>
      </div>
    </SettingsTask>
  );
}
