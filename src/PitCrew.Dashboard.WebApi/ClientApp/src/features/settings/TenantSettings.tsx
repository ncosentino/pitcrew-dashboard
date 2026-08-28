import { DisplayNameEditor } from '@/components/DisplayNameEditor';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { renameTenant } from './settingsApi';
import { SettingsTask } from './SettingsTask';

/** Props for owner-managed tenant settings. */
export interface TenantSettingsProps {
  readonly tenantId: string;
  readonly displayName: string;
  readonly antiforgeryToken: string;
  readonly onRenamed: (displayName: string) => void;
}

/** Allows a tenant owner to change the operator-facing name while preserving its stable ID. */
export function TenantSettings({
  tenantId,
  displayName,
  antiforgeryToken,
  onRenamed,
}: TenantSettingsProps) {
  return (
    <SettingsTask
      title="Tenant identity"
      description="Change the operator-facing name. The stable tenant ID in the administration context remains unchanged."
    >
      <OperationalList label="Tenant identity settings">
        <OperationalRow
          title="Tenant display name"
          description={displayName}
          status={<StatusBadge status="Editable" tone="neutral" />}
        >
          <DisplayNameEditor
            value={displayName}
            label="Tenant display name"
            submitLabel="Rename tenant"
            successMessage="Tenant name updated."
            onSave={async (nextDisplayName) => {
              await renameTenant(tenantId, nextDisplayName, antiforgeryToken);
              onRenamed(nextDisplayName);
            }}
          />
        </OperationalRow>
      </OperationalList>
    </SettingsTask>
  );
}
