import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { CopyableId } from '@/core/ui/CopyableId';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { EmptyState } from '@/core/ui/EmptyState';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ImageRecipeRegistrationForm } from './ImageRecipeRegistrationForm';
import { useImageWorkspace } from './imageWorkspaceContext';
import { disableImageRecipeRegistration, type ImageRecipeRegistration } from './imagesApi';

/** Presents frozen trusted workflow registrations and lower-frequency administration. */
export default function ImageRecipesPage() {
  const { tenantId, antiforgeryToken, canAdminister, data, error, isLoading, refresh } =
    useImageWorkspace();
  const [searchParams, setSearchParams] = useSearchParams();
  const detailHeading = useRef<HTMLHeadingElement>(null);
  const pendingFocus = useRef<string | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [disablingId, setDisablingId] = useState<string | null>(null);
  const registrations = [...(data?.registrations ?? [])].sort(
    (left, right) =>
      Number(left.disabledAt !== null) - Number(right.disabledAt !== null) ||
      Date.parse(right.createdAt) - Date.parse(left.createdAt),
  );
  const requestedId = searchParams.get('recipe');
  const requestedRegistration =
    requestedId == null
      ? undefined
      : registrations.find((registration) => registration.registrationId === requestedId);
  const selectedRegistration =
    requestedId == null ? (registrations[0] ?? null) : (requestedRegistration ?? null);
  const missingSelection =
    data !== null && requestedId !== null && requestedRegistration === undefined;

  useEffect(() => {
    if (requestedId !== null || registrations[0] === undefined) return;
    setSearchParams(
      (current) => {
        if (current.get('recipe')) return current;
        const next = new URLSearchParams(current);
        next.set('recipe', registrations[0].registrationId);
        return next;
      },
      { replace: true },
    );
  }, [registrations, requestedId, setSearchParams]);

  useEffect(() => {
    if (selectedRegistration?.registrationId !== pendingFocus.current) return;
    pendingFocus.current = null;
    detailHeading.current?.focus();
  }, [selectedRegistration]);

  const selectRegistration = (registrationId: string) => {
    pendingFocus.current = registrationId;
  };

  const disable = async (registration: ImageRecipeRegistration) => {
    if (disablingId !== null || registration.disabledAt !== null) return;
    setDisablingId(registration.registrationId);
    setMutationError(null);
    try {
      await disableImageRecipeRegistration(tenantId, registration.registrationId, antiforgeryToken);
      refresh();
    } catch (caught) {
      setMutationError(
        caught instanceof Error ? caught.message : 'The image recipe could not be disabled.',
      );
    } finally {
      setDisablingId(null);
    }
  };

  const onCreated = (registration: ImageRecipeRegistration) => {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.set('recipe', registration.registrationId);
        return next;
      },
      { replace: false },
    );
    pendingFocus.current = registration.registrationId;
    refresh();
  };

  if (isLoading) return <LoadingState label="Loading trusted image recipes…" />;
  if (!data) {
    return error ? null : (
      <EmptyState
        title="Recipe evidence unavailable"
        description="The recipe workspace cannot load without an authoritative API response."
      />
    );
  }

  return (
    <section aria-labelledby="image-recipes-heading" className="grid min-w-0 gap-4">
      <div>
        <h2 id="image-recipes-heading" className="text-lg font-semibold">
          Trusted image recipes
        </h2>
        <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
          Frozen GitHub workflow authority and source policy. Disabling a registration preserves its
          immutable audit history.
        </p>
      </div>
      {mutationError ? <StateBanner tone="critical">{mutationError}</StateBanner> : null}
      {data.registrationsTruncated ? (
        <StateBanner tone="caution">
          This bounded view shows the newest 100 recipe registrations. Older versions are not
          included.
        </StateBanner>
      ) : null}
      {missingSelection ? (
        <StateBanner tone="caution" role="status">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>
              The requested recipe registration is not present. Another registration has not been
              substituted.
            </span>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() =>
                setSearchParams(
                  (current) => {
                    const next = new URLSearchParams(current);
                    next.delete('recipe');
                    return next;
                  },
                  { replace: true },
                )
              }
            >
              Clear selection
            </Button>
          </div>
        </StateBanner>
      ) : null}

      {registrations.length === 0 ? (
        <EmptyState
          title="No trusted image recipes"
          description={
            canAdminister
              ? 'Register one reviewed GitHub workflow before requesting candidate builds.'
              : 'No administrator has registered a trusted image workflow for this tenant.'
          }
        />
      ) : (
        <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(19rem,0.9fr)_minmax(0,1.1fr)] xl:items-start">
          <OperationalList label="Trusted image recipe registrations">
            {registrations.map((registration) => {
              const selected = registration.registrationId === selectedRegistration?.registrationId;
              const next = new URLSearchParams(searchParams);
              next.set('recipe', registration.registrationId);
              return (
                <OperationalRow
                  key={`${registration.registrationId}-${registration.version}`}
                  selected={selected}
                  title={`${registration.recipeId} · version ${registration.version}`}
                  description={`${registration.repositoryOwner}/${registration.repositoryName} · ${registration.workflowPath}`}
                  status={
                    <StatusBadge
                      status={registration.disabledAt ? 'disabled' : 'enabled'}
                      tone={registration.disabledAt ? 'neutral' : 'positive'}
                    />
                  }
                  metadata={
                    <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
                      <span>Created {formatTime(registration.createdAt)}</span>
                      <span>{registration.allowedSourceRefs.length} allowed source refs</span>
                      <span>{registration.inputs.length} declared inputs</span>
                    </div>
                  }
                  actions={
                    <Button asChild size="sm" variant={selected ? 'secondary' : 'outline'}>
                      <Link
                        aria-current={selected ? 'page' : undefined}
                        to={`?${next.toString()}`}
                        onClick={() => selectRegistration(registration.registrationId)}
                      >
                        {selected ? 'Selected' : 'Inspect'}
                      </Link>
                    </Button>
                  }
                />
              );
            })}
          </OperationalList>

          {selectedRegistration ? (
            <div aria-label="Selected image recipe evidence" className="min-w-0" role="region">
              <RecipeDetail
                registration={selectedRegistration}
                canAdminister={canAdminister}
                disabling={disablingId === selectedRegistration.registrationId}
                focusTitleRef={detailHeading}
                onDisable={() => disable(selectedRegistration)}
              />
            </div>
          ) : null}
        </div>
      )}

      {canAdminister ? (
        <ImageRecipeRegistrationForm
          tenantId={tenantId}
          antiforgeryToken={antiforgeryToken}
          onCreated={onCreated}
        />
      ) : (
        <p className="text-sm text-muted-foreground">
          Viewer access is read-only. Tenant administrators register or disable recipes.
        </p>
      )}
    </section>
  );
}

function RecipeDetail({
  registration,
  canAdminister,
  disabling,
  focusTitleRef,
  onDisable,
}: {
  readonly registration: ImageRecipeRegistration;
  readonly canAdminister: boolean;
  readonly disabling: boolean;
  readonly focusTitleRef: React.Ref<HTMLHeadingElement>;
  readonly onDisable: () => Promise<void>;
}) {
  const [disableOpen, setDisableOpen] = useState(false);
  const [disableAcknowledged, setDisableAcknowledged] = useState(false);

  return (
    <DetailPanel
      title={`${registration.recipeId} · version ${registration.version}`}
      description={`${registration.repositoryOwner}/${registration.repositoryName}`}
      focusTitleRef={focusTitleRef}
      status={
        <StatusBadge
          status={registration.disabledAt ? 'disabled' : 'enabled'}
          tone={registration.disabledAt ? 'neutral' : 'positive'}
        />
      }
      actions={
        canAdminister && registration.disabledAt === null ? (
          <ConfirmActionDialog
            trigger={
              <Button type="button" size="sm" variant="outline" disabled={disabling}>
                {disabling ? 'Disabling…' : 'Disable registration'}
              </Button>
            }
            title={`Disable ${registration.recipeId}?`}
            description="Prevent new requests from using this exact registration version while preserving prior evidence."
            confirmLabel="Disable registration"
            confirmDisabled={!disableAcknowledged || disabling}
            open={disableOpen}
            onOpenChange={(open) => {
              setDisableOpen(open);
              if (!open) setDisableAcknowledged(false);
            }}
            details={
              <ConfirmationSummary
                identity={[
                  { label: 'Recipe', value: registration.recipeId },
                  { label: 'Version', value: registration.version },
                  {
                    label: 'Registration ID',
                    value: registration.registrationId,
                  },
                  { label: 'Workflow', value: registration.workflowPath },
                ]}
                fences={[
                  {
                    label: 'Registration version',
                    value: `${registration.registrationId} · version ${registration.version}`,
                  },
                  {
                    label: 'Workflow blob',
                    value: registration.workflowBlobSha,
                  },
                ]}
                effects={[
                  'Prevents new image build requests from using this registration version.',
                  'Preserves prior requests, candidates, qualifications, and audit history.',
                ]}
                prohibitedEffects={[
                  'Does not cancel active GitHub workflow runs.',
                  'Does not disable another registration version or delete candidate evidence.',
                  'Does not change any runner host or profile.',
                ]}
                acknowledgement={{
                  label: 'I verified the exact registration version and workflow blob to disable.',
                  checked: disableAcknowledged,
                  onCheckedChange: setDisableAcknowledged,
                }}
              />
            }
            onConfirm={onDisable}
          />
        ) : null
      }
    >
      <div className="grid min-w-0 gap-5">
        <section aria-labelledby={`recipe-authority-${registration.registrationId}`}>
          <h3
            id={`recipe-authority-${registration.registrationId}`}
            className="text-sm font-semibold"
          >
            Frozen workflow authority
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <Fact
              label="Registration ID"
              value={
                <CopyableId
                  value={registration.registrationId}
                  label="image recipe registration ID"
                />
              }
            />
            <Fact label="Version" value={registration.version} />
            <Fact label="Installation ID" value={registration.githubInstallationId} />
            <Fact label="Repository ID" value={registration.githubRepositoryId} />
            <Fact label="Workflow ID" value={registration.githubWorkflowId} />
            <Fact label="Workflow path" value={registration.workflowPath} />
            <Fact
              label="Workflow blob"
              value={
                <CopyableId value={registration.workflowBlobSha} label="workflow blob identity" />
              }
            />
            <Fact label="Dispatch ref" value={registration.dispatchRef} />
            <Fact label="Candidate schema" value={registration.candidateSchemaVersion} />
            <Fact label="Created" value={formatTime(registration.createdAt)} />
          </dl>
        </section>

        <section aria-labelledby={`recipe-sources-${registration.registrationId}`}>
          <h3
            id={`recipe-sources-${registration.registrationId}`}
            className="text-sm font-semibold"
          >
            Allowed source refs
          </h3>
          <ul className="mt-3 grid gap-2">
            {registration.allowedSourceRefs.map((sourceRef) => (
              <li
                className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 font-mono text-xs [overflow-wrap:anywhere]"
                key={sourceRef}
              >
                {sourceRef}
              </li>
            ))}
          </ul>
        </section>

        <section aria-labelledby={`recipe-inputs-${registration.registrationId}`}>
          <h3 id={`recipe-inputs-${registration.registrationId}`} className="text-sm font-semibold">
            Declared inputs
          </h3>
          {registration.inputs.length === 0 ? (
            <p className="mt-2 text-sm text-muted-foreground">
              No additional workflow inputs are declared.
            </p>
          ) : (
            <ul className="mt-3 divide-y overflow-hidden rounded-lg border">
              {registration.inputs.map((input) => (
                <li className="grid gap-1 px-3 py-2" key={input.name}>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-mono text-sm font-medium">{input.name}</span>
                    <StatusBadge status={input.required ? 'required' : 'optional'} tone="neutral" />
                  </div>
                  <p className="text-xs text-muted-foreground">
                    {input.type}
                    {input.maxLength ? ` · maximum length ${input.maxLength}` : ''}
                    {input.allowedValues ? ` · ${input.allowedValues.length} allowed values` : ''}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </section>

        {registration.disabledAt ? (
          <StateBanner tone="caution">
            Disabled {formatTime(registration.disabledAt)} by GitHub user{' '}
            {registration.disabledByGitHubUserId ?? 'Unavailable'}.
          </StateBanner>
        ) : null}
      </div>
    </DetailPanel>
  );
}

function Fact({ label, value }: { readonly label: string; readonly value: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 min-w-0 [overflow-wrap:anywhere] text-sm font-medium">{value}</dd>
    </div>
  );
}
