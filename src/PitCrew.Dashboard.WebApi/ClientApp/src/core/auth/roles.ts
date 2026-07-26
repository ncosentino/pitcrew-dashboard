import type { TenantRole } from './sessionApi';

const roleRanks: Record<TenantRole, number> = {
  viewer: 0,
  administrator: 1,
  owner: 2,
};

/** Returns whether a tenant role satisfies a route or navigation requirement. */
export function hasMinimumTenantRole(role: TenantRole, minimumRole: TenantRole): boolean {
  return roleRanks[role] >= roleRanks[minimumRole];
}
