import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const positiveDecimalSchema = z.string().regex(/^[1-9][0-9]*$/u);
const sha1Schema = z.string().regex(/^[0-9a-f]{40}$/u);
const recipeInputTypeSchema = z.enum(['string', 'integer', 'number', 'boolean']);

export const imageRecipeInputSchema = z.object({
  name: z.string().min(1).max(64),
  type: recipeInputTypeSchema,
  required: z.boolean(),
  maxLength: z.number().int().positive().nullable(),
  allowedValues: z.array(z.string().max(256)).nullable(),
});
export const imageRecipeInputsSchema = z.array(imageRecipeInputSchema).max(16);

export const imageRecipeRegistrationSchema = z.object({
  registrationId: z.string().uuid(),
  version: z.number().int().positive(),
  githubInstallationId: positiveDecimalSchema,
  githubRepositoryId: positiveDecimalSchema,
  githubWorkflowId: positiveDecimalSchema,
  repositoryOwner: z.string().min(1).max(39),
  repositoryName: z.string().min(1).max(100),
  workflowPath: z.string().min(1).max(256),
  workflowBlobSha: sha1Schema,
  dispatchRef: z.string().min(1).max(256),
  recipeId: z.string().min(1).max(64),
  candidateSchemaVersion: z.number().int().positive(),
  allowedSourceRefs: z.array(z.string().min(1).max(256)),
  inputs: imageRecipeInputsSchema,
  createdByGitHubUserId: positiveDecimalSchema,
  createdAt: offsetDateTimeSchema,
  disabledByGitHubUserId: positiveDecimalSchema.nullable(),
  disabledAt: offsetDateTimeSchema.nullable(),
});
export const imageRecipeRegistrationListSchema = z.object({
  registrations: z.array(imageRecipeRegistrationSchema),
  truncated: z.boolean(),
});

export const imageBuildStatusSchema = z.enum([
  'requested',
  'dispatching',
  'building',
  'qualifying',
  'ready',
  'blocked',
  'failed',
]);
export const imageBuildRequestSchema = z.object({
  requestId: z.string().uuid(),
  registrationId: z.string().uuid(),
  registrationVersion: z.number().int().positive(),
  recipeId: z.string().min(1).max(64),
  sourceRepository: z.string().min(1).max(256),
  sourceRef: z.string().min(1).max(256),
  sourceCommit: sha1Schema,
  status: imageBuildStatusSchema,
  githubRunId: positiveDecimalSchema.nullable(),
  githubRunApiUrl: z.string().url().nullable(),
  githubRunHtmlUrl: z.string().url().nullable(),
  terminalCategory: z.string().min(1).max(128).nullable(),
  terminalDetail: z.string().min(1).max(512).nullable(),
  requestedAt: offsetDateTimeSchema,
  updatedAt: offsetDateTimeSchema,
});
export const imageBuildRequestListSchema = z.object({
  requests: z.array(imageBuildRequestSchema),
  truncated: z.boolean(),
});

export const imageCandidateQualificationSchema = z.object({
  name: z.enum([
    'image-build',
    'buildkit-digest',
    'registry-digest',
    'oci-manifest',
    'builder-cleanup',
  ]),
  status: z.enum(['passed', 'failed', 'unavailable']),
});
export const imageCandidateSchema = z.object({
  candidateId: z.string().uuid(),
  requestId: z.string().uuid(),
  registrationId: z.string().uuid(),
  registrationVersion: z.number().int().positive(),
  outcome: z.enum(['ready', 'failed']),
  recipeId: z.string().min(1).max(64),
  sourceRepository: z.string().min(1).max(256),
  sourceCommit: sha1Schema,
  githubRunId: positiveDecimalSchema,
  githubRunApiUrl: z.string().url().nullable(),
  githubRunUrl: z.string().url().nullable(),
  artifactId: positiveDecimalSchema,
  artifactName: z.literal('pitcrew-image-candidate'),
  artifactDigest: z.string().min(1).max(256),
  reportHash: z.string().min(1).max(256),
  imageReference: z.string().min(1).max(2048),
  digest: z.string().min(1).max(256).nullable(),
  immutableReference: z.string().min(1).max(2048).nullable(),
  platform: z.enum(['linux/amd64', 'linux/arm64']),
  outputMode: z.enum(['registry', 'oci']),
  failureCategory: z.string().min(1).max(128).nullable(),
  failureDetail: z.string().min(1).max(512).nullable(),
  createdAt: offsetDateTimeSchema,
  storedAt: offsetDateTimeSchema,
  qualifications: z.array(imageCandidateQualificationSchema),
});
export const imageCandidateListSchema = z.object({
  candidates: z.array(imageCandidateSchema),
  truncated: z.boolean(),
});

export type ImageRecipeInput = z.infer<typeof imageRecipeInputSchema>;
export type ImageRecipeRegistration = z.infer<typeof imageRecipeRegistrationSchema>;
export type ImageRecipeRegistrationList = z.infer<typeof imageRecipeRegistrationListSchema>;
export type ImageBuildStatus = z.infer<typeof imageBuildStatusSchema>;
export type ImageBuildRequest = z.infer<typeof imageBuildRequestSchema>;
export type ImageBuildRequestList = z.infer<typeof imageBuildRequestListSchema>;
export type ImageCandidateQualification = z.infer<typeof imageCandidateQualificationSchema>;
export type ImageCandidate = z.infer<typeof imageCandidateSchema>;
export type ImageCandidateList = z.infer<typeof imageCandidateListSchema>;

export interface CreateImageRecipeRegistrationInput {
  readonly registrationId: string;
  readonly githubInstallationId: string;
  readonly githubRepositoryId: string;
  readonly githubWorkflowId: string;
  readonly workflowPath: string;
  readonly dispatchRef: string;
  readonly recipeId: string;
  readonly candidateSchemaVersion: 1;
  readonly allowedSourceRefs: ReadonlyArray<string>;
  readonly inputs: ReadonlyArray<ImageRecipeInput>;
}

export interface CreateImageBuildRequestInput {
  readonly requestId: string;
  readonly registrationId: string;
  readonly registrationVersion: number;
  readonly sourceRef: string;
  readonly sourceCommit: string;
  readonly inputs: Readonly<Record<string, unknown>>;
}

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

function tenantImagesPath(tenantId: string): string {
  return `/api/tenants/${encodeURIComponent(tenantId)}/images`;
}

/** Lists frozen trusted image recipe registrations. */
export async function getImageRecipeRegistrations(
  tenantId: string,
  signal: AbortSignal,
): Promise<ImageRecipeRegistrationList> {
  return await createClient().request(
    `${tenantImagesPath(tenantId)}/v1/recipes/registrations?limit=100&includeDisabled=true`,
    {
      method: 'GET',
      schema: imageRecipeRegistrationListSchema,
      signal,
    },
  );
}

/** Lists durable image build requests newest first. */
export async function getImageBuildRequests(
  tenantId: string,
  signal: AbortSignal,
): Promise<ImageBuildRequestList> {
  return await createClient().request(`${tenantImagesPath(tenantId)}/requests?limit=100`, {
    method: 'GET',
    schema: imageBuildRequestListSchema,
    signal,
  });
}

/** Lists immutable ready or failed image candidates newest first. */
export async function getImageCandidates(
  tenantId: string,
  signal: AbortSignal,
): Promise<ImageCandidateList> {
  return await createClient().request(`${tenantImagesPath(tenantId)}/candidates?limit=100`, {
    method: 'GET',
    schema: imageCandidateListSchema,
    signal,
  });
}

/** Loads one immutable image candidate by candidate ID. */
export async function getImageCandidate(
  tenantId: string,
  candidateId: string,
  signal: AbortSignal,
): Promise<ImageCandidate> {
  return await createClient().request(
    `${tenantImagesPath(tenantId)}/candidates/${encodeURIComponent(candidateId)}`,
    {
      method: 'GET',
      schema: imageCandidateSchema,
      signal,
    },
  );
}

/** Registers one frozen trusted workflow authority. */
export async function createImageRecipeRegistration(
  tenantId: string,
  input: CreateImageRecipeRegistrationInput,
  antiforgeryToken: string,
): Promise<ImageRecipeRegistration> {
  return await createClient().request(`${tenantImagesPath(tenantId)}/v1/recipes/registrations`, {
    method: 'POST',
    body: input,
    headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    schema: imageRecipeRegistrationSchema,
  });
}

/** Disables one recipe registration without rewriting its audit history. */
export async function disableImageRecipeRegistration(
  tenantId: string,
  registrationId: string,
  antiforgeryToken: string,
): Promise<ImageRecipeRegistration> {
  return await createClient().request(
    `${tenantImagesPath(tenantId)}/v1/recipes/registrations/${encodeURIComponent(registrationId)}/disable`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      schema: imageRecipeRegistrationSchema,
    },
  );
}

/** Creates one durable request for an exact source commit and registration version. */
export async function createImageBuildRequest(
  tenantId: string,
  input: CreateImageBuildRequestInput,
  antiforgeryToken: string,
): Promise<ImageBuildRequest> {
  return await createClient().request(`${tenantImagesPath(tenantId)}/requests`, {
    method: 'POST',
    body: input,
    headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    schema: imageBuildRequestSchema,
  });
}
