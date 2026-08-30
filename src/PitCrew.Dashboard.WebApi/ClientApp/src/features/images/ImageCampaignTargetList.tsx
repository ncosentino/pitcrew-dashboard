import { Link } from 'react-router-dom';

import { CopyableId } from '@/core/ui/CopyableId';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { ImageCampaignTarget } from './imageCampaignApi';
import {
  campaignTargetAttentionRank,
  campaignTargetTone,
  formatCampaignState,
} from './imageCampaignView';

interface ImageCampaignTargetListProps {
  readonly tenantId: string;
  readonly targets: ReadonlyArray<ImageCampaignTarget>;
}

/** Presents frozen eligible and excluded campaign targets without hiding either set. */
export function ImageCampaignTargetList({ tenantId, targets }: ImageCampaignTargetListProps) {
  const included = targets
    .filter((target) => target.exclusionCategory === null)
    .sort(compareTargets);
  const excluded = targets
    .filter((target) => target.exclusionCategory !== null)
    .sort(compareTargets);

  return (
    <div className="grid min-w-0 gap-5">
      <TargetSection
        tenantId={tenantId}
        title="Campaign targets"
        description="Frozen eligible targets remain in the approved campaign even when later evidence changes."
        empty="No target passed the frozen eligibility calculation."
        targets={included}
      />
      <TargetSection
        tenantId={tenantId}
        title="Excluded targets"
        description="Excluded node/profile identities stay visible with the exact planning reason."
        empty="No target was excluded from this campaign."
        targets={excluded}
      />
    </div>
  );
}

function TargetSection({
  tenantId,
  title,
  description,
  empty,
  targets,
}: {
  readonly tenantId: string;
  readonly title: string;
  readonly description: string;
  readonly empty: string;
  readonly targets: ReadonlyArray<ImageCampaignTarget>;
}) {
  return (
    <section className="grid min-w-0 gap-3" aria-labelledby={`campaign-${slug(title)}-heading`}>
      <div>
        <h3 className="text-base font-semibold" id={`campaign-${slug(title)}-heading`}>
          {title}
        </h3>
        <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">{description}</p>
      </div>
      {targets.length === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <OperationalList label={title}>
          {targets.map((target) => (
            <OperationalRow
              key={target.targetId}
              title={`${target.nodeDisplayName} · ${target.profileId}`}
              description={
                target.exclusionCategory
                  ? formatCampaignState(target.exclusionCategory)
                  : (target.candidate?.targetDigest ?? 'Target authority unavailable')
              }
              status={
                <StatusBadge
                  status={formatCampaignState(target.status)}
                  tone={campaignTargetTone(target.status)}
                />
              }
              metadata={
                <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                  {target.isCanary ? <span className="font-semibold">Canary</span> : null}
                  {target.waveNumber === null ? null : <span>Wave {target.waveNumber}</span>}
                  {target.candidate ? (
                    <>
                      <span>{target.candidate.recipeId}</span>
                      <span>{target.candidate.targetPlatform}</span>
                      <CopyableId
                        label="Target candidate ID"
                        value={target.candidate.candidateId}
                      />
                    </>
                  ) : null}
                  {target.currentWorkers === null || target.staleWorkers === null ? null : (
                    <span>
                      {target.currentWorkers} current · {target.staleWorkers} stale
                    </span>
                  )}
                  {target.commandId ? (
                    <CopyableId label="Profile rollout command ID" value={target.commandId} />
                  ) : null}
                </div>
              }
              actions={
                <Link
                  className="inline-flex min-h-8 items-center rounded-md border px-3 text-sm font-medium hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                  to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(target.nodeId)}/profiles/${encodeURIComponent(target.profileId)}/image`}
                >
                  Profile evidence
                </Link>
              }
            >
              {target.failureCategory || target.resultMessage ? (
                <p className="text-xs text-muted-foreground">
                  {[target.failureCategory, target.resultMessage].filter(Boolean).join(' · ')}
                </p>
              ) : null}
            </OperationalRow>
          ))}
        </OperationalList>
      )}
    </section>
  );
}

function compareTargets(left: ImageCampaignTarget, right: ImageCampaignTarget): number {
  const rank = campaignTargetAttentionRank(left) - campaignTargetAttentionRank(right);
  return (
    rank ||
    (left.waveNumber ?? Number.MAX_SAFE_INTEGER) - (right.waveNumber ?? Number.MAX_SAFE_INTEGER) ||
    left.nodeDisplayName.localeCompare(right.nodeDisplayName) ||
    left.profileId.localeCompare(right.profileId)
  );
}

function slug(value: string): string {
  return value.toLowerCase().replaceAll(' ', '-');
}
