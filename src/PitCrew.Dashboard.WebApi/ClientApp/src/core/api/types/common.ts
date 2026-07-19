import { z } from 'zod';

/** Standard error envelope returned by APIs on non-2xx responses. */
export const errorEnvelopeSchema = z.object({
  error: z.object({
    code: z.string(),
    message: z.string(),
    detail: z.unknown().optional(),
  }),
});

export type ErrorEnvelope = z.infer<typeof errorEnvelopeSchema>;

/** Generic paginated envelope `{ items, nextCursor? }`. */
export function paginatedSchema<T extends z.ZodTypeAny>(item: T) {
  return z.object({
    items: z.array(item),
    nextCursor: z.string().optional(),
  });
}

export interface Paginated<T> {
  items: T[];
  nextCursor?: string;
}
