import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const digestSchema = z.string().regex(/^sha256:[0-9a-f]{64}$/u);
const hashSchema = z.string().regex(/^[0-9a-f]{64}$/u);

export const maximumImageCampaignWaveSize = 100;

export const imageCampaignKindSchema = z.enum(['forward', 'rollback']);
export const imageCampaignStatusSchema = z.enum([
  'draft',
  'awaiting-approval',
  'running',
  'paused',
  'complete',
  'partial',
  'blocked',
  'cancelled',
]);
export const imageCampaignTargetStatusSchema = z.enum([
  'eligible',
  'excluded',
  'queued',
  'claimed',
  'applying',
  'rolling',
  'complete',
  'failed',
  'blocked',
  'indeterminate',
  'cancelled',
]);
export const imageCampaignWaveStatusSchema = z.enum([
  'pending',
  'approved',
  'running',
  'complete',
  'blocked',
  'cancelled',
]);
export const imageCampaignExclusionSchema = z
  .enum([
    'node-offline',
    'node-revoked',
    'capability-unavailable',
    'stale-observed-state',
    'unsupported-schema',
    'unsupported-manager',
    'unsupported-topology',
    'unsupported-architecture',
    'recipe-not-allowed',
    'registry-not-allowed',
    'policy-disabled',
    'operation-active',
    'already-current',
    'insufficient-evidence',
    'rollback-authority-unavailable',
  ])
  .nullable();

export const imageCampaignCandidateSchema = z.object({
  candidateId: z.string().uuid(),
  recipeId: z.string().min(1).max(100),
  targetDigest: digestSchema,
  targetPlatform: z.enum(['linux/amd64', 'linux/arm64']),
});

export const imageCampaignWaveSchema = z.object({
  waveNumber: z.number().int().nonnegative(),
  status: imageCampaignWaveStatusSchema,
  targetCount: z.number().int().positive(),
  approvedByGitHubUserId: z.string().min(1).max(64).nullable(),
  approvedAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
});

export const imageCampaignTargetSchema = z.object({
  targetId: z.string().uuid(),
  nodeId: z.string().uuid(),
  nodeDisplayName: z.string().min(1).max(128),
  profileId: z.string().min(1).max(32),
  candidate: imageCampaignCandidateSchema.nullable(),
  expectedCurrentImageReference: z.string().min(1).max(512).nullable(),
  expectedCurrentImageDigest: digestSchema.nullable(),
  expectedCurrentLocalImageId: digestSchema.nullable(),
  expectedCurrentWorkerRevision: hashSchema.nullable(),
  expectedStaticFingerprint: hashSchema.nullable(),
  expectedPreservedConfigurationFingerprint: hashSchema.nullable(),
  expectedRoutingFingerprint: hashSchema.nullable(),
  expectedDesiredGeneration: z.number().int().nonnegative().nullable(),
  expectedDesiredStateHash: hashSchema.nullable(),
  exclusionCategory: imageCampaignExclusionSchema,
  status: imageCampaignTargetStatusSchema,
  waveNumber: z.number().int().nonnegative().nullable(),
  isCanary: z.boolean(),
  commandId: z.string().uuid().nullable(),
  failureCategory: z.string().min(1).max(64).nullable(),
  resultMessage: z.string().min(1).max(512).nullable(),
  targetWorkerRevision: hashSchema.nullable(),
  managerConvergenceStatus: z.enum(['current', 'rolling', 'degraded']).nullable(),
  currentWorkers: z.number().int().nonnegative().nullable(),
  staleWorkers: z.number().int().nonnegative().nullable(),
  claimedAt: offsetDateTimeSchema.nullable(),
  startedAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
  previousCandidateId: z.string().uuid().nullable(),
  previousRecipeId: z.string().min(1).max(100).nullable(),
  previousImageReference: z.string().min(1).max(512).nullable(),
  previousImageDigest: digestSchema.nullable(),
  previousWorkerRevision: hashSchema.nullable(),
});

export const imageCampaignSummarySchema = z.object({
  campaignId: z.string().uuid(),
  kind: imageCampaignKindSchema,
  sourceCampaignId: z.string().uuid().nullable(),
  candidate: imageCampaignCandidateSchema.nullable(),
  targetSetHash: hashSchema,
  status: imageCampaignStatusSchema,
  revision: z.number().int().nonnegative(),
  waveSize: z.number().int().positive().nullable(),
  eligibleTargetCount: z.number().int().nonnegative(),
  excludedTargetCount: z.number().int().nonnegative(),
  completeTargetCount: z.number().int().nonnegative(),
  adverseTargetCount: z.number().int().nonnegative(),
  currentWaveNumber: z.number().int().nonnegative().nullable(),
  nextWaveNumber: z.number().int().nonnegative().nullable(),
  requestedByGitHubUserId: z.string().min(1).max(64),
  requestedAt: offsetDateTimeSchema,
  configuredAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
});

export const imageCampaignSchema = z.object({
  campaignId: z.string().uuid(),
  kind: imageCampaignKindSchema,
  sourceCampaignId: z.string().uuid().nullable(),
  candidate: imageCampaignCandidateSchema.nullable(),
  targetSetHash: hashSchema,
  status: imageCampaignStatusSchema,
  revision: z.number().int().nonnegative(),
  waveSize: z.number().int().positive().nullable(),
  requestedByGitHubUserId: z.string().min(1).max(64),
  requestedAt: offsetDateTimeSchema,
  configuredByGitHubUserId: z.string().min(1).max(64).nullable(),
  configuredAt: offsetDateTimeSchema.nullable(),
  pausedAt: offsetDateTimeSchema.nullable(),
  cancelledAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
  targets: z.array(imageCampaignTargetSchema).max(5000),
  waves: z.array(imageCampaignWaveSchema).max(5000),
});

export const imageCampaignListSchema = z.object({
  campaigns: z.array(imageCampaignSummarySchema).max(100),
  truncated: z.boolean(),
});

export type ImageCampaign = z.infer<typeof imageCampaignSchema>;
export type ImageCampaignSummary = z.infer<typeof imageCampaignSummarySchema>;
export type ImageCampaignTarget = z.infer<typeof imageCampaignTargetSchema>;
export type ImageCampaignStatus = z.infer<typeof imageCampaignStatusSchema>;
export type ImageCampaignTargetStatus = z.infer<typeof imageCampaignTargetStatusSchema>;
export type ImageCampaignWave = z.infer<typeof imageCampaignWaveSchema>;

export interface ConfigureImageCampaignInput {
  readonly canaryTargetId: string | null;
  readonly waveSize: number;
  readonly expectedRevision: number;
  readonly expectedTargetSetHash: string;
}

export interface ImageCampaignMutationFence {
  readonly expectedRevision: number;
  readonly expectedTargetSetHash: string;
}

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

function campaignsPath(tenantId: string): string {
  return `/api/tenants/${encodeURIComponent(tenantId)}/images/campaigns`;
}

function campaignPath(tenantId: string, campaignId: string): string {
  return `${campaignsPath(tenantId)}/${encodeURIComponent(campaignId)}`;
}

function mutationHeaders(idempotencyKey: string, antiforgeryToken: string) {
  return {
    'Idempotency-Key': idempotencyKey,
    'X-PitCrew-Antiforgery': antiforgeryToken,
  };
}

/** Loads newest tenant campaign summaries. */
export async function getImageCampaigns(
  tenantId: string,
  signal: AbortSignal,
): Promise<z.infer<typeof imageCampaignListSchema>> {
  return await createClient().request(`${campaignsPath(tenantId)}?limit=100`, {
    method: 'GET',
    schema: imageCampaignListSchema,
    signal,
  });
}

/** Loads one frozen campaign with target and wave evidence. */
export async function getImageCampaign(
  tenantId: string,
  campaignId: string,
  signal: AbortSignal,
): Promise<ImageCampaign> {
  return await createClient().request(campaignPath(tenantId, campaignId), {
    method: 'GET',
    schema: imageCampaignSchema,
    signal,
  });
}

/** Creates one frozen forward campaign draft from a ready candidate. */
export async function createImageCampaign(
  tenantId: string,
  candidateId: string,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await createClient().request(campaignsPath(tenantId), {
    method: 'POST',
    body: { candidateId },
    headers: mutationHeaders(idempotencyKey, antiforgeryToken),
    schema: imageCampaignSchema,
  });
}

/** Freezes canary and deterministic wave assignment for one draft. */
export async function configureImageCampaign(
  tenantId: string,
  campaignId: string,
  input: ConfigureImageCampaignInput,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await createClient().request(`${campaignPath(tenantId, campaignId)}/configure`, {
    method: 'POST',
    body: input,
    headers: mutationHeaders(idempotencyKey, antiforgeryToken),
    schema: imageCampaignSchema,
  });
}

/** Approves one exact pending campaign wave. */
export async function approveImageCampaignWave(
  tenantId: string,
  campaignId: string,
  waveNumber: number,
  fence: ImageCampaignMutationFence,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await createClient().request(
    `${campaignPath(tenantId, campaignId)}/waves/${waveNumber}/approve`,
    {
      method: 'POST',
      body: fence,
      headers: mutationHeaders(idempotencyKey, antiforgeryToken),
      schema: imageCampaignSchema,
    },
  );
}

async function mutateCampaign(
  tenantId: string,
  campaignId: string,
  action: 'pause' | 'resume' | 'cancel',
  fence: ImageCampaignMutationFence,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await createClient().request(`${campaignPath(tenantId, campaignId)}/${action}`, {
    method: 'POST',
    body: fence,
    headers: mutationHeaders(idempotencyKey, antiforgeryToken),
    schema: imageCampaignSchema,
  });
}

/** Pauses future target dispatch while existing profile commands continue. */
export async function pauseImageCampaign(
  tenantId: string,
  campaignId: string,
  fence: ImageCampaignMutationFence,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await mutateCampaign(
    tenantId,
    campaignId,
    'pause',
    fence,
    idempotencyKey,
    antiforgeryToken,
  );
}

/** Resumes a paused campaign at its active wave or next approval gate. */
export async function resumeImageCampaign(
  tenantId: string,
  campaignId: string,
  fence: ImageCampaignMutationFence,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await mutateCampaign(
    tenantId,
    campaignId,
    'resume',
    fence,
    idempotencyKey,
    antiforgeryToken,
  );
}

/** Cancels every campaign target that has no durable profile command. */
export async function cancelImageCampaign(
  tenantId: string,
  campaignId: string,
  fence: ImageCampaignMutationFence,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await mutateCampaign(
    tenantId,
    campaignId,
    'cancel',
    fence,
    idempotencyKey,
    antiforgeryToken,
  );
}

/** Creates a separate rollback draft from proven per-target prior authority. */
export async function createImageCampaignRollback(
  tenantId: string,
  campaignId: string,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<ImageCampaign> {
  return await createClient().request(`${campaignPath(tenantId, campaignId)}/rollback`, {
    method: 'POST',
    headers: mutationHeaders(idempotencyKey, antiforgeryToken),
    schema: imageCampaignSchema,
  });
}
