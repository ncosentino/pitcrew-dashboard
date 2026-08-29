import { useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { FormField } from '@/core/ui/FormField';
import { StateBanner } from '@/core/ui/StateBanner';

import {
  createImageRecipeRegistration,
  type ImageRecipeInput,
  type ImageRecipeRegistration,
} from './imagesApi';

interface ImageRecipeRegistrationFormProps {
  readonly tenantId: string;
  readonly antiforgeryToken: string;
  readonly onCreated: (registration: ImageRecipeRegistration) => void;
}

interface DraftRecipeInput {
  readonly key: string;
  readonly name: string;
  readonly type: ImageRecipeInput['type'];
  readonly required: boolean;
  readonly maxLength: string;
  readonly allowedValues: string;
}

function newDraftInput(): DraftRecipeInput {
  return {
    key: globalThis.crypto.randomUUID(),
    name: '',
    type: 'string',
    required: false,
    maxLength: '',
    allowedValues: '',
  };
}

function numericIdentity(value: string): boolean {
  return /^[1-9][0-9]*$/u.test(value);
}

/** Registers one reviewed workflow authority without exposing arbitrary dispatch controls. */
export function ImageRecipeRegistrationForm({
  tenantId,
  antiforgeryToken,
  onCreated,
}: ImageRecipeRegistrationFormProps) {
  const [registrationId, setRegistrationId] = useState(() => globalThis.crypto.randomUUID());
  const [installationId, setInstallationId] = useState('');
  const [repositoryId, setRepositoryId] = useState('');
  const [workflowId, setWorkflowId] = useState('');
  const [workflowPath, setWorkflowPath] = useState('');
  const [dispatchRef, setDispatchRef] = useState('');
  const [recipeId, setRecipeId] = useState('');
  const [allowedSourceRefs, setAllowedSourceRefs] = useState('');
  const [inputs, setInputs] = useState<ReadonlyArray<DraftRecipeInput>>([]);
  const [acknowledged, setAcknowledged] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sourceRefs = allowedSourceRefs
    .split(/\r?\n/u)
    .map((value) => value.trim())
    .filter((value, index, values) => value !== '' && values.indexOf(value) === index);
  const inputNames = inputs.map((input) => input.name);
  const inputsValid =
    new Set(inputNames).size === inputNames.length &&
    inputs.every(
      (input) =>
        /^[A-Za-z][A-Za-z0-9_-]{0,63}$/u.test(input.name) &&
        !['pitcrew_request_id', 'pitcrew_source_commit', 'pitcrew_recipe_id'].includes(
          input.name,
        ) &&
        (input.maxLength === '' ||
          (Number.isInteger(Number(input.maxLength)) && Number(input.maxLength) > 0)),
    );
  const valid =
    numericIdentity(installationId) &&
    numericIdentity(repositoryId) &&
    numericIdentity(workflowId) &&
    workflowPath.trim() !== '' &&
    dispatchRef.trim() !== '' &&
    /^[a-z][a-z0-9-]{0,63}$/u.test(recipeId) &&
    sourceRefs.length > 0 &&
    inputsValid;

  const updateInput = (key: string, update: Partial<DraftRecipeInput>) => {
    setInputs((current) =>
      current.map((input) => (input.key === key ? { ...input, ...update } : input)),
    );
  };

  const submit = async () => {
    if (!valid || isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const registration = await createImageRecipeRegistration(
        tenantId,
        {
          registrationId,
          githubInstallationId: installationId,
          githubRepositoryId: repositoryId,
          githubWorkflowId: workflowId,
          workflowPath: workflowPath.trim(),
          dispatchRef: dispatchRef.trim(),
          recipeId,
          candidateSchemaVersion: 1,
          allowedSourceRefs: sourceRefs,
          inputs: inputs.map((input) => ({
            name: input.name,
            type: input.type,
            required: input.required,
            maxLength: input.maxLength === '' ? null : Number(input.maxLength),
            allowedValues:
              input.allowedValues.trim() === ''
                ? null
                : input.allowedValues
                    .split(',')
                    .map((value) => value.trim())
                    .filter(
                      (value, index, values) => value !== '' && values.indexOf(value) === index,
                    ),
          })),
        },
        antiforgeryToken,
      );
      setRegistrationId(globalThis.crypto.randomUUID());
      setInstallationId('');
      setRepositoryId('');
      setWorkflowId('');
      setWorkflowPath('');
      setDispatchRef('');
      setRecipeId('');
      setAllowedSourceRefs('');
      setInputs([]);
      setAcknowledged(false);
      onCreated(registration);
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : 'The trusted image recipe could not be registered.',
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <details className="rounded-xl border bg-card">
      <summary className="cursor-pointer list-none rounded-xl px-4 py-3 text-sm font-semibold outline-none focus-visible:ring-2 focus-visible:ring-ring sm:px-5">
        Register a trusted image recipe
      </summary>
      <div className="grid gap-4 border-t p-4 sm:p-5">
        {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
        <p className="max-w-[72ch] text-sm text-muted-foreground">
          Registration freezes one GitHub installation, repository, workflow revision, dispatch ref,
          recipe, source policy, and declared non-secret input schema.
        </p>
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          <FormField label="GitHub installation ID">
            <input
              className="h-11 rounded-md border bg-background px-3 text-sm"
              inputMode="numeric"
              value={installationId}
              onChange={(event) => setInstallationId(event.target.value.trim())}
            />
          </FormField>
          <FormField label="GitHub repository ID">
            <input
              className="h-11 rounded-md border bg-background px-3 text-sm"
              inputMode="numeric"
              value={repositoryId}
              onChange={(event) => setRepositoryId(event.target.value.trim())}
            />
          </FormField>
          <FormField label="GitHub workflow ID">
            <input
              className="h-11 rounded-md border bg-background px-3 text-sm"
              inputMode="numeric"
              value={workflowId}
              onChange={(event) => setWorkflowId(event.target.value.trim())}
            />
          </FormField>
          <FormField label="Workflow path">
            <input
              className="h-11 rounded-md border bg-background px-3 text-sm"
              placeholder=".github/workflows/build-runner-image.yml"
              value={workflowPath}
              onChange={(event) => setWorkflowPath(event.target.value)}
            />
          </FormField>
          <FormField label="Frozen dispatch ref">
            <input
              className="h-11 rounded-md border bg-background px-3 text-sm"
              placeholder="refs/heads/main"
              value={dispatchRef}
              onChange={(event) => setDispatchRef(event.target.value)}
            />
          </FormField>
          <FormField
            label="Recipe ID"
            hint="Lowercase letters, numbers, and hyphens; starts with a letter."
          >
            <input
              className="h-11 rounded-md border bg-background px-3 text-sm"
              value={recipeId}
              onChange={(event) => setRecipeId(event.target.value.trim().toLowerCase())}
            />
          </FormField>
        </div>
        <FormField
          label="Allowed source refs"
          hint="One exact branch or tag ref per line. At least one is required."
        >
          <textarea
            className="min-h-24 rounded-md border bg-background px-3 py-2 font-mono text-sm"
            placeholder={'refs/heads/main\nrefs/tags/v1'}
            value={allowedSourceRefs}
            onChange={(event) => setAllowedSourceRefs(event.target.value)}
          />
        </FormField>

        <fieldset className="grid gap-3 border-t pt-4">
          <legend className="sr-only">Declared workflow inputs</legend>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm font-semibold">Declared workflow inputs</p>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={inputs.length >= 16}
              onClick={() => setInputs((current) => [...current, newDraftInput()])}
            >
              Add input
            </Button>
          </div>
          {inputs.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No additional inputs. Dashboard-owned request, source commit, and recipe inputs remain
              reserved.
            </p>
          ) : (
            <div className="grid gap-3">
              {inputs.map((input, index) => (
                <fieldset className="grid gap-3 rounded-lg border bg-muted/15 p-3" key={input.key}>
                  <legend className="px-1 text-xs font-semibold">Input {index + 1}</legend>
                  <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                    <FormField label="Name">
                      <input
                        className="h-10 rounded-md border bg-background px-3 text-sm"
                        value={input.name}
                        onChange={(event) => updateInput(input.key, { name: event.target.value })}
                      />
                    </FormField>
                    <FormField label="Type">
                      <select
                        className="h-10 rounded-md border bg-background px-3 text-sm"
                        value={input.type}
                        onChange={(event) =>
                          updateInput(input.key, {
                            type: event.target.value as ImageRecipeInput['type'],
                          })
                        }
                      >
                        <option value="string">String</option>
                        <option value="integer">Integer</option>
                        <option value="number">Number</option>
                        <option value="boolean">Boolean</option>
                      </select>
                    </FormField>
                    <FormField label="Maximum length" hint="Optional positive integer.">
                      <input
                        className="h-10 rounded-md border bg-background px-3 text-sm"
                        inputMode="numeric"
                        value={input.maxLength}
                        onChange={(event) =>
                          updateInput(input.key, { maxLength: event.target.value.trim() })
                        }
                      />
                    </FormField>
                    <FormField label="Allowed values" hint="Optional comma-separated closed set.">
                      <input
                        className="h-10 rounded-md border bg-background px-3 text-sm"
                        value={input.allowedValues}
                        onChange={(event) =>
                          updateInput(input.key, { allowedValues: event.target.value })
                        }
                      />
                    </FormField>
                  </div>
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <label className="flex items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={input.required}
                        onChange={(event) =>
                          updateInput(input.key, { required: event.target.checked })
                        }
                      />
                      Required
                    </label>
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() =>
                        setInputs((current) => current.filter((item) => item.key !== input.key))
                      }
                    >
                      Remove input
                    </Button>
                  </div>
                </fieldset>
              ))}
            </div>
          )}
        </fieldset>

        <div className="flex flex-wrap items-end justify-between gap-3 border-t pt-4">
          <p className="max-w-[62ch] text-xs text-muted-foreground">
            Registration ID {registrationId}. Failed submissions retain this idempotency key.
          </p>
          <ConfirmActionDialog
            trigger={
              <Button type="button" disabled={!valid || isSubmitting}>
                {isSubmitting ? 'Registering…' : 'Review recipe registration'}
              </Button>
            }
            title="Register this trusted workflow authority?"
            description="Validate and freeze this exact workflow revision and source policy."
            confirmLabel="Register trusted recipe"
            confirmDisabled={!acknowledged || isSubmitting}
            details={
              <ConfirmationSummary
                identity={[
                  { label: 'Recipe', value: recipeId || 'Unavailable' },
                  { label: 'Workflow', value: workflowPath || 'Unavailable' },
                  { label: 'Dispatch ref', value: dispatchRef || 'Unavailable' },
                  { label: 'Allowed source refs', value: sourceRefs.join(', ') || 'Unavailable' },
                ]}
                fences={[
                  { label: 'Installation ID', value: installationId || 'Unavailable' },
                  { label: 'Repository ID', value: repositoryId || 'Unavailable' },
                  { label: 'Workflow ID', value: workflowId || 'Unavailable' },
                  { label: 'Candidate schema', value: '1' },
                ]}
                effects={[
                  'Validates the GitHub installation and workflow, then freezes one versioned recipe registration.',
                  `Allows only ${inputs.length} declared non-secret input ${inputs.length === 1 ? 'field' : 'fields'} in addition to reserved Dashboard inputs.`,
                ]}
                prohibitedEffects={[
                  'Does not dispatch a workflow or build an image.',
                  'Does not grant arbitrary repository, workflow, ref, input, registry, host, or command authority.',
                  'Does not change or delete prior registration versions.',
                ]}
                acknowledgement={{
                  label:
                    'I reviewed the exact GitHub identities, workflow path, source refs, and declared inputs.',
                  checked: acknowledged,
                  onCheckedChange: setAcknowledged,
                }}
              />
            }
            onConfirm={submit}
          />
        </div>
      </div>
    </details>
  );
}
