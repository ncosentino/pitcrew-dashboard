import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const positiveDecimalSchema = z.string().regex(/^[1-9][0-9]*$/u);
const sha1Schema = z.string().regex(/^[0-9a-f]{40}$/u);

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

export type ImageCandidateQualification = z.infer<typeof imageCandidateQualificationSchema>;
export type ImageCandidate = z.infer<typeof imageCandidateSchema>;
export type ImageCandidateList = z.infer<typeof imageCandidateListSchema>;

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

function tenantImagesPath(tenantId: string): string {
  return `/api/tenants/${encodeURIComponent(tenantId)}/images`;
}

/** Lists the newest bounded immutable candidate evidence for one tenant. */
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

/** Loads one tenant-owned immutable candidate by its stable candidate ID. */
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
