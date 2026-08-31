import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { ApiError } from '@/core/api/httpClient';
import { formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { Button } from '@/components/ui/button';

import {
  createImageCampaign,
  getImageCampaign,
  getImageCampaigns,
  type ImageCampaign,
  type ImageCampaignSummary,
} from './imageCampaignApi';
import { ImageCampaignDetail } from './ImageCampaignDetail';
import { useImageWorkspace } from './imageWorkspaceContext';
import {
  campaignAttentionRank,
  campaignStatusTone,
  formatCampaignState,
} from './imageCampaignView';

/** Runs frozen image rollout campaign planning and progress inside Runner images. */
export default function ImageCampaignsPage() {
  const { campaignId } = useParams();
  const navigate = useNavigate();
  const { tenantId, antiforgeryToken, canAdminister, data, error, isLoading } = useImageWorkspace();
  const readyCandidates = useMemo(
    () =>
      data?.candidates.filter(
        (candidate) =>
          candidate.outcome === 'ready' &&
          candidate.outputMode === 'registry' &&
          candidate.digest !== null,
      ) ?? [],
    [data?.candidates],
  );
  const [candidateId, setCandidateId] = useState(readyCandidates[0]?.candidateId ?? '');
  const [selected, setSelected] = useState<ImageCampaign | null>(null);
  const [missingCampaignId, setMissingCampaignId] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<{
    readonly campaignId: string;
    readonly message: string;
  } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [createKey, setCreateKey] = useState(() => globalThis.crypto.randomUUID());
  const [campaignSummaries, setCampaignSummaries] =
    useState<ReadonlyArray<ImageCampaignSummary> | null>(null);
  const [campaignListError, setCampaignListError] = useState<string | null>(null);
  const [campaignsTruncated, setCampaignsTruncated] = useState(false);
  const [campaignRefreshVersion, setCampaignRefreshVersion] = useState(0);

  const selectedCandidateId = readyCandidates.some(
    (candidate) => candidate.candidateId === candidateId,
  )
    ? candidateId
    : (readyCandidates[0]?.candidateId ?? '');
  const selectedCampaign = selected?.campaignId === campaignId ? selected : null;
  const selectedMissing = missingCampaignId === campaignId;
  const selectedDetailError =
    detailError !== null && detailError.campaignId === campaignId ? detailError.message : null;
  const candidateEvidenceUnavailable = data === null && error !== null;

  useEffect(() => {
    const controller = new AbortController();
    let requestVersion = 0;
    const load = async () => {
      const version = requestVersion + 1;
      requestVersion = version;
      try {
        const list = await getImageCampaigns(tenantId, controller.signal);
        if (controller.signal.aborted || requestVersion !== version) return;
        setCampaignSummaries(list.campaigns);
        setCampaignsTruncated(list.truncated);
        setCampaignListError(null);
      } catch (caught) {
        if (controller.signal.aborted || requestVersion !== version) return;
        setCampaignListError(
          caught instanceof Error ? caught.message : 'Campaign list could not be loaded.',
        );
      }
    };
    void load();
    const timer = globalThis.setInterval(() => void load(), 8_000);
    return () => {
      globalThis.clearInterval(timer);
      controller.abort();
    };
  }, [campaignRefreshVersion, tenantId]);

  useEffect(() => {
    if (!campaignId) return;
    const controller = new AbortController();
    let requestVersion = 0;
    const load = async () => {
      const version = requestVersion + 1;
      requestVersion = version;
      try {
        const campaign = await getImageCampaign(tenantId, campaignId, controller.signal);
        if (controller.signal.aborted || requestVersion !== version) return;
        setSelected(campaign);
        setMissingCampaignId(null);
        setDetailError(null);
      } catch (caught) {
        if (controller.signal.aborted || requestVersion !== version) return;
        if (caught instanceof ApiError && caught.status === 404) {
          setMissingCampaignId(campaignId);
          setDetailError(null);
          return;
        }
        setDetailError({
          campaignId,
          message:
            caught instanceof Error ? caught.message : 'Campaign evidence could not be loaded.',
        });
      }
    };
    void load();
    const timer = globalThis.setInterval(() => void load(), 8_000);
    return () => {
      globalThis.clearInterval(timer);
      controller.abort();
    };
  }, [campaignId, tenantId]);

  const campaigns = useMemo(
    () =>
      [...(campaignSummaries ?? [])].sort((left, right) => {
        const rank = campaignAttentionRank(left) - campaignAttentionRank(right);
        return rank || right.requestedAt.localeCompare(left.requestedAt);
      }),
    [campaignSummaries],
  );

  const create = async () => {
    if (!canAdminister || selectedCandidateId === '' || submitting) return;
    let requestKey = createKey;
    if (candidateId !== selectedCandidateId) {
      requestKey = globalThis.crypto.randomUUID();
      setCandidateId(selectedCandidateId);
      setCreateKey(requestKey);
    }
    setSubmitting(true);
    setMutationError(null);
    try {
      const campaign = await createImageCampaign(
        tenantId,
        selectedCandidateId,
        requestKey,
        antiforgeryToken,
      );
      setSelected(campaign);
      setCreateKey(globalThis.crypto.randomUUID());
      setCampaignRefreshVersion((current) => current + 1);
      navigate(`/tenants/${encodeURIComponent(tenantId)}/images/campaigns/${campaign.campaignId}`);
    } catch (caught) {
      setMutationError(
        caught instanceof Error ? caught.message : 'The campaign draft could not be created.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  if (isLoading) return <LoadingState label="Loading image rollout campaigns" />;

  return (
    <div className="grid min-w-0 gap-5">
      <section className="grid gap-4 rounded-xl border bg-card p-4 sm:p-5">
        <div>
          <h2 className="text-lg font-semibold">Plan a frozen campaign</h2>
          <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
            Freeze one ready candidate against every current node/profile target before choosing a
            canary or granting host authority.
          </p>
        </div>
        {candidateEvidenceUnavailable ? (
          <StateBanner tone="critical">
            Ready candidate evidence is unavailable. Campaign planning remains disabled until the
            candidate API recovers.
          </StateBanner>
        ) : readyCandidates.length === 0 ? (
          <StateBanner tone="caution">
            No ready registry candidate is available. Qualify one in Candidates before planning a
            campaign.
          </StateBanner>
        ) : canAdminister ? (
          <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
            <FormField label="Ready candidate">
              <select
                className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
                value={selectedCandidateId}
                onChange={(event) => {
                  setCandidateId(event.target.value);
                  setCreateKey(globalThis.crypto.randomUUID());
                }}
              >
                {readyCandidates.map((candidate) => (
                  <option key={candidate.candidateId} value={candidate.candidateId}>
                    {candidate.recipeId} · {candidate.digest}
                  </option>
                ))}
              </select>
            </FormField>
            <Button
              disabled={selectedCandidateId === '' || submitting || error !== null}
              type="button"
              onClick={() => void create()}
            >
              {submitting ? 'Freezing…' : 'Freeze campaign plan'}
            </Button>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            Viewer access is read-only. Campaign planning controls are available to tenant
            administrators.
          </p>
        )}
        {mutationError ? <StateBanner tone="critical">{mutationError}</StateBanner> : null}
      </section>

      {selectedMissing ? (
        <StateBanner tone={selectedCampaign ? 'caution' : 'critical'}>
          {selectedCampaign
            ? 'Showing retained campaign evidence because the selected campaign is no longer returned by the detail API. Mutations remain disabled.'
            : "The selected campaign is not present in this tenant's retained evidence. No substitute campaign was selected."}
        </StateBanner>
      ) : null}
      {campaignListError ? (
        <StateBanner tone={campaignSummaries ? 'caution' : 'critical'}>
          {campaignSummaries
            ? `Showing retained campaign list because refresh failed: ${campaignListError}`
            : `Campaign list is unavailable: ${campaignListError}`}
        </StateBanner>
      ) : null}
      {selectedDetailError ? (
        <StateBanner tone={selectedCampaign ? 'caution' : 'critical'}>
          {selectedCampaign
            ? `Showing retained campaign evidence because refresh failed: ${selectedDetailError}`
            : `Campaign evidence is unavailable: ${selectedDetailError}`}
        </StateBanner>
      ) : null}

      <div className="grid min-w-0 gap-5 xl:grid-cols-[minmax(18rem,0.8fr)_minmax(0,1.6fr)]">
        <section
          className="grid min-w-0 content-start gap-3"
          aria-labelledby="campaign-list-heading"
        >
          <div>
            <h2 className="text-base font-semibold" id="campaign-list-heading">
              Rollout campaigns
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Attention-ordered frozen plans and retained terminal history.
            </p>
          </div>
          {campaignsTruncated ? (
            <p className="text-xs text-muted-foreground">
              This bounded view shows the newest 100 campaigns.
            </p>
          ) : null}
          {campaignSummaries === null && campaignListError === null ? (
            <LoadingState label="Loading image rollout campaign list" />
          ) : campaigns.length === 0 ? (
            <EmptyState
              title="No image rollout campaigns"
              description="Freeze a ready candidate against the current fleet to create the first draft."
            />
          ) : (
            <OperationalList label="Image rollout campaigns">
              {campaigns.map((campaign) => (
                <CampaignRow
                  key={campaign.campaignId}
                  campaign={campaign}
                  selected={campaign.campaignId === campaignId}
                  tenantId={tenantId}
                />
              ))}
            </OperationalList>
          )}
        </section>

        <div className="min-w-0">
          {!campaignId ? (
            <EmptyState
              title="Select a campaign"
              description="Open one frozen plan to review targets, approvals, and per-target convergence."
            />
          ) : selectedCampaign ? (
            <ImageCampaignDetail
              key={`${selectedCampaign.campaignId}:${selectedCampaign.revision}`}
              tenantId={tenantId}
              campaign={selectedCampaign}
              canAdminister={canAdminister}
              antiforgeryToken={antiforgeryToken}
              refreshBlocked={selectedDetailError !== null || selectedMissing}
              onCampaignChanged={(campaign) => {
                setSelected(campaign);
                setCampaignRefreshVersion((current) => current + 1);
                if (campaign.campaignId !== campaignId) {
                  navigate(
                    `/tenants/${encodeURIComponent(tenantId)}/images/campaigns/${campaign.campaignId}`,
                  );
                }
              }}
            />
          ) : selectedMissing || selectedDetailError ? null : (
            <LoadingState label="Loading selected image rollout campaign" />
          )}
        </div>
      </div>
    </div>
  );
}

function CampaignRow({
  campaign,
  selected,
  tenantId,
}: {
  readonly campaign: ImageCampaignSummary;
  readonly selected: boolean;
  readonly tenantId: string;
}) {
  return (
    <OperationalRow
      selected={selected}
      title={
        campaign.kind === 'forward'
          ? (campaign.candidate?.recipeId ?? 'Image campaign')
          : 'Rollback campaign'
      }
      description={`${campaign.eligibleTargetCount} eligible · ${campaign.excludedTargetCount} excluded`}
      status={
        <StatusBadge
          status={formatCampaignState(campaign.status)}
          tone={campaignStatusTone(campaign.status)}
        />
      }
      metadata={
        <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
          <span>{formatTime(campaign.requestedAt)}</span>
          {campaign.currentWaveNumber === null ? null : (
            <span>Wave {campaign.currentWaveNumber}</span>
          )}
          {campaign.adverseTargetCount > 0 ? (
            <span>{campaign.adverseTargetCount} adverse</span>
          ) : null}
        </div>
      }
      actions={
        <Link
          aria-current={selected ? 'page' : undefined}
          className="inline-flex min-h-8 items-center rounded-md border px-3 text-sm font-medium hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          to={`/tenants/${encodeURIComponent(tenantId)}/images/campaigns/${encodeURIComponent(campaign.campaignId)}`}
        >
          {selected ? 'Selected' : 'Open'}
        </Link>
      }
    />
  );
}
