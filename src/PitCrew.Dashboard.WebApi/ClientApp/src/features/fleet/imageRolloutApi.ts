import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const digestSchema = z.string().regex(/^sha256:[0-9a-f]{64}$/u);
const sha256Schema = z.string().regex(/^[0-9a-f]{64}$/u);
const positiveDecimalSchema = z.string().regex(/^[1-9][0-9]*$/u);

export const imageRolloutCommandStatusSchema = z.enum([
  'queued',
  'claimed',
  'started',
  'succeeded',
  'rejected',
  'failed',
  'expired',
  'indeterminate',
]);

export const imageRolloutFailureCategorySchema = z
  .enum([
    'not-allowed',
    'recipe-not-allowed',
    'registry-not-allowed',
    'stale-fence',
    'expired',
    'unsupported',
    'unsupported-architecture',
    'unsupported-topology',
    'operation-active',
    'timeout',
    'process-failure',
    'unknown',
  ])
  .nullable();

export const profileImageRolloutCommandSchema = z.object({
  commandId: z.string().uuid(),
  candidateId: z.string().uuid(),
  recipeId: z.string().min(1).max(100),
  targetDigest: digestSchema,
  targetPlatform: z.enum(['linux/amd64', 'linux/arm64']),
  previousImageReference: z.string().min(1).max(512).nullable(),
  previousImageDigest: digestSchema.nullable(),
  previousWorkerRevision: sha256Schema.nullable(),
  status: imageRolloutCommandStatusSchema,
  failureCategory: imageRolloutFailureCategorySchema,
  requestedByGitHubUserId: positiveDecimalSchema,
  requestedAt: offsetDateTimeSchema,
  expiresAt: offsetDateTimeSchema,
  deliveredAt: offsetDateTimeSchema.nullable(),
  claimedAt: offsetDateTimeSchema.nullable(),
  startedAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
  targetWorkerRevision: sha256Schema.nullable(),
  managerConvergenceStatus: z.enum(['current', 'rolling', 'degraded']).nullable(),
  currentWorkers: z.number().int().nonnegative().nullable(),
  staleWorkers: z.number().int().nonnegative().nullable(),
  lastError: z.string().min(1).max(128).nullable(),
  resultMessage: z.string().min(1).max(512).nullable(),
  previousCandidateId: z.string().uuid().nullable(),
  previousRecipeId: z.string().min(1).max(100).nullable(),
});

export const profileImageRolloutControlSchema = z.object({
  nodeId: z.string().uuid(),
  profileId: z.string().min(1).max(32),
  architecture: z.enum(['linux/amd64', 'linux/arm64']),
  currentImageReference: z.string().min(1).max(512).nullable(),
  currentImageDigest: digestSchema.nullable(),
  currentLocalImageId: digestSchema.nullable(),
  currentWorkerRevision: sha256Schema.nullable(),
  staticFingerprint: sha256Schema,
  preservedConfigurationFingerprint: sha256Schema,
  routingFingerprint: sha256Schema,
  desiredGeneration: z.number().int().nonnegative(),
  desiredStateHash: sha256Schema.nullable(),
  allowedRecipeIds: z.array(z.string().min(1).max(100)).max(64),
  rolloutAllowed: z.boolean(),
  localSchemaSupported: z.boolean(),
  localFailureCategory: z
    .enum([
      'unsupported-architecture',
      'unsupported-schema',
      'unsupported-manager',
      'unsupported-topology',
      'not-allowed',
      'policy-disabled',
      'recipe-not-allowed',
      'registry-not-allowed',
      'stale-observed-state',
    ])
    .nullable(),
  operationActive: z.boolean(),
  observedStateAgeSeconds: z.number().int().nonnegative().max(86_400),
  observedStateMaximumAgeSeconds: z.number().int().positive().max(3_600),
  observedStateFresh: z.boolean(),
  managerConvergenceStatus: z.enum(['current', 'rolling', 'degraded']),
  currentWorkers: z.number().int().nonnegative().nullable(),
  staleWorkers: z.number().int().nonnegative().nullable(),
  latestCommand: profileImageRolloutCommandSchema.nullable(),
  recentCommands: z.array(profileImageRolloutCommandSchema).max(20),
});

export const rollOutProfileImageResponseSchema = z.object({
  commandId: z.string().uuid(),
  status: z.literal('queued'),
  statusLocation: z.string().min(1).max(512),
});

export type ImageRolloutCommandStatus = z.infer<typeof imageRolloutCommandStatusSchema>;
export type ProfileImageRolloutCommand = z.infer<typeof profileImageRolloutCommandSchema>;
export type ProfileImageRolloutControl = z.infer<typeof profileImageRolloutControlSchema>;
export type RollOutProfileImageResponse = z.infer<typeof rollOutProfileImageResponseSchema>;

export interface RollOutProfileImageInput {
  readonly nodeId: string;
  readonly profileId: string;
  readonly candidateId: string;
  readonly expectedCurrentImageReference: string | null;
  readonly expectedCurrentImageDigest: string | null;
  readonly expectedCurrentLocalImageId: string | null;
  readonly expectedCurrentWorkerRevision: string | null;
  readonly expectedStaticFingerprint: string;
  readonly expectedPreservedConfigurationFingerprint: string;
  readonly expectedRoutingFingerprint: string;
  readonly expectedDesiredGeneration: number;
  readonly expectedDesiredStateHash: string | null;
}

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

function profileRolloutPath(tenantId: string, nodeId: string, profileId: string): string {
  return `/api/tenants/${encodeURIComponent(tenantId)}/images/profile-rollouts/${encodeURIComponent(nodeId)}/${encodeURIComponent(profileId)}`;
}

/** Loads one profile's current rollout capability and bounded command history. */
export async function getProfileImageRollout(
  tenantId: string,
  nodeId: string,
  profileId: string,
  signal: AbortSignal,
): Promise<ProfileImageRolloutControl> {
  return await createClient().request(profileRolloutPath(tenantId, nodeId, profileId), {
    method: 'GET',
    schema: profileImageRolloutControlSchema,
    signal,
  });
}

/** Queues one exact candidate-to-profile changeover with a stable idempotency key. */
export async function rollOutProfileImage(
  tenantId: string,
  input: RollOutProfileImageInput,
  idempotencyKey: string,
  antiforgeryToken: string,
): Promise<RollOutProfileImageResponse> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/images/profile-rollouts`,
    {
      method: 'POST',
      body: input,
      headers: {
        'Idempotency-Key': idempotencyKey,
        'X-PitCrew-Antiforgery': antiforgeryToken,
      },
      schema: rollOutProfileImageResponseSchema,
    },
  );
}
