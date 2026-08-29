import { useMemo, useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { FormField } from '@/core/ui/FormField';
import { StateBanner } from '@/core/ui/StateBanner';

import {
  createImageBuildRequest,
  type ImageRecipeInput,
  type ImageRecipeRegistration,
} from './imagesApi';

interface ImageBuildRequestFormProps {
  readonly tenantId: string;
  readonly antiforgeryToken: string;
  readonly registrations: ReadonlyArray<ImageRecipeRegistration>;
  readonly onCreated: (requestId: string) => void;
}

function initialInputValues(
  registration: ImageRecipeRegistration | null,
): Readonly<Record<string, string>> {
  return Object.fromEntries(
    (registration?.inputs ?? []).map((input) => [
      input.name,
      input.required ? (input.allowedValues?.[0] ?? (input.type === 'boolean' ? 'false' : '')) : '',
    ]),
  );
}

function parseInputValue(input: ImageRecipeInput, value: string): unknown {
  if (input.type === 'boolean') return value === 'true';
  if (input.type === 'integer' || input.type === 'number') return Number(value);
  return value;
}

/** Composes one exact candidate build request and confirms its frozen authority. */
export function ImageBuildRequestForm({
  tenantId,
  antiforgeryToken,
  registrations,
  onCreated,
}: ImageBuildRequestFormProps) {
  const enabled = useMemo(
    () => registrations.filter((registration) => registration.disabledAt === null),
    [registrations],
  );
  const firstRegistration = enabled[0] ?? null;
  const [requestId, setRequestId] = useState(() => globalThis.crypto.randomUUID());
  const [registrationId, setRegistrationId] = useState(
    () => firstRegistration?.registrationId ?? '',
  );
  const [sourceRef, setSourceRef] = useState(() => firstRegistration?.allowedSourceRefs[0] ?? '');
  const [sourceCommit, setSourceCommit] = useState('');
  const [inputValues, setInputValues] = useState<Readonly<Record<string, string>>>(() =>
    initialInputValues(firstRegistration),
  );
  const [acknowledged, setAcknowledged] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const registration =
    enabled.find((candidate) => candidate.registrationId === registrationId) ?? firstRegistration;

  const chooseRegistration = (nextRegistrationId: string) => {
    const next = enabled.find((candidate) => candidate.registrationId === nextRegistrationId);
    setRegistrationId(nextRegistrationId);
    setSourceRef(next?.allowedSourceRefs[0] ?? '');
    setInputValues(initialInputValues(next ?? null));
    setAcknowledged(false);
  };

  const inputsValid =
    registration !== null &&
    registration.inputs.every((input) => {
      const value = inputValues[input.name] ?? '';
      if (input.required && value === '') return false;
      if (input.maxLength !== null && value.length > input.maxLength) return false;
      if (
        value !== '' &&
        (input.type === 'integer' || input.type === 'number') &&
        !Number.isFinite(Number(value))
      ) {
        return false;
      }
      if (value !== '' && input.type === 'integer' && !Number.isInteger(Number(value))) {
        return false;
      }
      return true;
    });
  const requestValid =
    registration !== null &&
    sourceRef !== '' &&
    /^[0-9a-f]{40}$/u.test(sourceCommit) &&
    inputsValid;

  const submit = async () => {
    if (!registration || !requestValid || isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      await createImageBuildRequest(
        tenantId,
        {
          requestId,
          registrationId: registration.registrationId,
          registrationVersion: registration.version,
          sourceRef,
          sourceCommit,
          inputs: Object.fromEntries(
            registration.inputs.flatMap((input) => {
              const value = inputValues[input.name] ?? '';
              return value === '' && !input.required
                ? []
                : [[input.name, parseInputValue(input, value)]];
            }),
          ),
        },
        antiforgeryToken,
      );
      const createdRequestId = requestId;
      setRequestId(globalThis.crypto.randomUUID());
      setSourceCommit('');
      setAcknowledged(false);
      onCreated(createdRequestId);
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'The image build request could not be created.',
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <details className="rounded-xl border bg-card">
      <summary className="cursor-pointer list-none rounded-xl px-4 py-3 text-sm font-semibold outline-none focus-visible:ring-2 focus-visible:ring-ring sm:px-5">
        Request a candidate build
      </summary>
      <div className="grid gap-4 border-t p-4 sm:p-5">
        {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
        {enabled.length === 0 ? (
          <StateBanner tone="caution">
            An enabled trusted recipe registration is required before requesting a build.
          </StateBanner>
        ) : (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="Trusted recipe">
                <select
                  className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
                  value={registration?.registrationId ?? ''}
                  onChange={(event) => chooseRegistration(event.target.value)}
                >
                  {enabled.map((candidate) => (
                    <option key={candidate.registrationId} value={candidate.registrationId}>
                      {candidate.recipeId} · v{candidate.version} · {candidate.repositoryOwner}/
                      {candidate.repositoryName}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Allowed source ref">
                <select
                  className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
                  value={sourceRef}
                  onChange={(event) => setSourceRef(event.target.value)}
                >
                  {(registration?.allowedSourceRefs ?? []).map((allowedRef) => (
                    <option key={allowedRef} value={allowedRef}>
                      {allowedRef}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <FormField
              label="Exact source commit"
              hint="Lowercase 40-character commit SHA reachable from the selected allowed ref."
            >
              <input
                className="h-11 min-w-0 rounded-md border bg-background px-3 font-mono text-sm"
                maxLength={40}
                pattern="[0-9a-f]{40}"
                required
                value={sourceCommit}
                onChange={(event) => setSourceCommit(event.target.value.trim().toLowerCase())}
              />
            </FormField>

            {registration && registration.inputs.length > 0 ? (
              <fieldset className="grid gap-4 border-t pt-4">
                <legend className="pr-2 text-sm font-semibold">Declared workflow inputs</legend>
                <div className="grid gap-4 sm:grid-cols-2">
                  {registration.inputs.map((input) => (
                    <RecipeValueField
                      input={input}
                      key={input.name}
                      value={inputValues[input.name] ?? ''}
                      onChange={(value) =>
                        setInputValues((current) => ({ ...current, [input.name]: value }))
                      }
                    />
                  ))}
                </div>
              </fieldset>
            ) : null}

            <div className="flex flex-wrap items-end justify-between gap-3 border-t pt-4">
              <p className="max-w-[62ch] text-xs text-muted-foreground">
                Request ID {requestId}. This idempotency key is retained if submission fails.
              </p>
              <ConfirmActionDialog
                trigger={
                  <Button type="button" disabled={!requestValid || isSubmitting}>
                    {isSubmitting ? 'Requesting…' : 'Review build request'}
                  </Button>
                }
                title="Dispatch this trusted image build?"
                description="Create one durable request for the exact reviewed workflow authority and source commit."
                confirmLabel="Request candidate build"
                confirmDisabled={!acknowledged || isSubmitting}
                details={
                  <ConfirmationSummary
                    identity={[
                      { label: 'Recipe', value: registration?.recipeId ?? 'Unavailable' },
                      {
                        label: 'Repository',
                        value: registration
                          ? `${registration.repositoryOwner}/${registration.repositoryName}`
                          : 'Unavailable',
                      },
                      { label: 'Source ref', value: sourceRef || 'Unavailable' },
                      { label: 'Source commit', value: sourceCommit || 'Unavailable' },
                    ]}
                    fences={[
                      {
                        label: 'Registration',
                        value: registration
                          ? `${registration.registrationId} · version ${registration.version}`
                          : 'Unavailable',
                      },
                      {
                        label: 'Workflow blob',
                        value: registration?.workflowBlobSha ?? 'Unavailable',
                      },
                    ]}
                    effects={[
                      'Creates one durable build request and dispatches only the frozen workflow with its declared inputs.',
                    ]}
                    prohibitedEffects={[
                      'Does not roll an image to any host or profile.',
                      'Does not grant arbitrary workflow, ref, input, Docker, registry, or command authority.',
                      'Does not automatically redispatch an indeterminate request.',
                    ]}
                    acknowledgement={{
                      label: 'I verified the exact source commit and reviewed recipe authority.',
                      checked: acknowledged,
                      onCheckedChange: setAcknowledged,
                    }}
                  />
                }
                onConfirm={submit}
              />
            </div>
          </>
        )}
      </div>
    </details>
  );
}

function RecipeValueField({
  input,
  value,
  onChange,
}: {
  readonly input: ImageRecipeInput;
  readonly value: string;
  readonly onChange: (value: string) => void;
}) {
  const hint = `${input.required ? 'Required' : 'Optional'} ${input.type} input${
    input.maxLength ? `, maximum ${input.maxLength} characters` : ''
  }.`;

  if (input.allowedValues && input.allowedValues.length > 0) {
    return (
      <FormField label={input.name} hint={hint}>
        <select
          className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
          value={value}
          onChange={(event) => onChange(event.target.value)}
        >
          {!input.required ? <option value="">Not supplied</option> : null}
          {input.allowedValues.map((allowed) => (
            <option key={allowed} value={allowed}>
              {allowed}
            </option>
          ))}
        </select>
      </FormField>
    );
  }

  if (input.type === 'boolean') {
    return (
      <FormField label={input.name} hint={hint}>
        <select
          className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
          value={value}
          onChange={(event) => onChange(event.target.value)}
        >
          {!input.required ? <option value="">Not supplied</option> : null}
          <option value="false">False</option>
          <option value="true">True</option>
        </select>
      </FormField>
    );
  }

  return (
    <FormField label={input.name} hint={hint}>
      <input
        className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
        maxLength={input.maxLength ?? undefined}
        required={input.required}
        type={input.type === 'string' ? 'text' : 'number'}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </FormField>
  );
}
