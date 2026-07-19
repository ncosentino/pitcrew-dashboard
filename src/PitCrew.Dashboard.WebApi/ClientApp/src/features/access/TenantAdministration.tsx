import { useCallback, useEffect, useState } from 'react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { ApiError } from '@/core/api/httpClient';

import {
  getAvailableUsers,
  getTenantMembers,
  removeTenantMembership,
  setTenantMembership,
  type DashboardUser,
  type TenantMember,
  type TenantRole,
} from './accessApi';

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

  const mutate = async (operation: () => Promise<void>) => {
    setIsBusy(true);
    try {
      await operation();
      await load(new AbortController().signal);
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

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tenant membership</CardTitle>
        <CardDescription>
          Users appear here after their first GitHub sign-in. A tenant always retains at least one
          owner.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        {error ? <p className="text-sm text-red-700 dark:text-red-300">{error}</p> : null}
        <div className="overflow-x-auto">
          <table className="w-full min-w-xl text-left text-sm">
            <thead className="text-xs text-muted-foreground uppercase">
              <tr>
                <th className="px-2 py-2">User</th>
                <th className="px-2 py-2">Role</th>
                <th className="px-2 py-2 text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {members.map((member) => (
                <tr key={member.user.githubUserId} className="border-t">
                  <td className="px-2 py-2">
                    <div className="font-medium">{member.user.displayName}</div>
                    <div className="text-xs text-muted-foreground">
                      @{member.user.githubLogin} · {member.user.githubUserId}
                    </div>
                  </td>
                  <td className="px-2 py-2 capitalize">{member.role}</td>
                  <td className="px-2 py-2 text-right">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={isBusy}
                      onClick={() =>
                        void mutate(() =>
                          removeTenantMembership(
                            tenantId,
                            member.user.githubUserId,
                            antiforgeryToken,
                          ),
                        )
                      }
                    >
                      Remove
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="grid gap-3 rounded-lg border p-4 sm:grid-cols-[1fr_auto_auto]">
          <select
            aria-label="User"
            className="h-9 rounded-md border bg-background px-3 text-sm"
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
          <select
            aria-label="Role"
            className="h-9 rounded-md border bg-background px-3 text-sm"
            value={selectedRole}
            onChange={(event) => setSelectedRole(event.target.value as TenantRole)}
          >
            <option value="viewer">Viewer</option>
            <option value="administrator">Administrator</option>
            <option value="owner">Owner</option>
          </select>
          <Button
            type="button"
            disabled={isBusy || selectedUserId.length === 0}
            onClick={() =>
              void mutate(() =>
                setTenantMembership(tenantId, selectedUserId, selectedRole, antiforgeryToken),
              )
            }
          >
            Add member
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
