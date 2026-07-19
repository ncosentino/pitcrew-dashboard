import { z, ZodError } from 'zod';
import { errorEnvelopeSchema, type ErrorEnvelope } from './types/common';

/**
 * Typed application error raised by the HTTP client when a request fails.
 * Carries the parsed `ErrorEnvelope` from the server plus the HTTP status.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail: unknown;
  readonly envelope: ErrorEnvelope;

  constructor(status: number, envelope: ErrorEnvelope) {
    super(envelope.error.message);
    this.name = 'ApiError';
    this.status = status;
    this.code = envelope.error.code;
    this.detail = envelope.error.detail;
    this.envelope = envelope;
  }
}

/** Raised when the server's response body fails zod schema validation. */
export class ApiParseError extends Error {
  readonly issues: ZodError['issues'];
  constructor(message: string, issues: ZodError['issues']) {
    super(message);
    this.name = 'ApiParseError';
    this.issues = issues;
  }
}

export interface HttpRequestInit<TSchema extends z.ZodTypeAny | undefined = undefined> {
  /** HTTP method. Defaults to GET. */
  readonly method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';
  /** Request body. Serialized as JSON unless `bodyKind === 'text'`. */
  readonly body?: unknown;
  /** If `'text'`, body is sent as-is with the supplied content-type. */
  readonly bodyKind?: 'json' | 'text';
  /** When bodyKind === 'text', the Content-Type to set. */
  readonly contentType?: string;
  /** Additional headers to send (merged with client defaults). */
  readonly headers?: Record<string, string>;
  /** Cancellation signal (typically from react-query or a controller). */
  readonly signal?: AbortSignal;
  /** Zod schema used to parse and validate the JSON response. */
  readonly schema?: TSchema;
  /** When true, return raw `Response` for text/etag handling. */
  readonly raw?: boolean;
}

export interface RawHttpResponse {
  readonly status: number;
  readonly headers: Headers;
  readonly text: string;
}

export interface HttpClientConfig {
  readonly baseUrl: string;
  readonly defaultHeaders?: Record<string, string>;
  /** Override fetch (used by tests). */
  readonly fetchImpl?: typeof fetch;
  /**
   * Auth hook applied before every request. Receives the assembled
   * `Request` (URL + headers + body) and returns the request to send.
   * The default is to leave the request unchanged. Providers may attach
   * `Authorization` headers, CSRF tokens, etc.
   */
  readonly attachAuth?: (request: Request) => Request;
}

function joinUrl(base: string, path: string): string {
  const trimmedBase = base.replace(/\/+$/, '');
  const normalisedPath = path.startsWith('/') ? path : `/${path}`;
  return `${trimmedBase}${normalisedPath}`;
}

async function parseErrorEnvelope(response: Response): Promise<ErrorEnvelope> {
  try {
    const text = await response.text();
    if (!text) {
      return synthesise(response.status);
    }
    const json = JSON.parse(text) as unknown;
    const parsed = errorEnvelopeSchema.safeParse(json);
    if (parsed.success) return parsed.data;
    return synthesise(response.status);
  } catch {
    return synthesise(response.status);
  }
}

function synthesise(status: number): ErrorEnvelope {
  return { error: { code: 'http_error', message: `HTTP ${status}` } };
}

/**
 * Centralized HTTP client.
 *
 * All HTTP traffic in this codebase SHOULD go through an HttpClient (or
 * a thin factory that returns one). Each call:
 * - Resolves the URL against `baseUrl`.
 * - Sends configured default headers + per-call headers + content type.
 * - Wires the supplied `AbortSignal` through to `fetch`.
 * - On non-2xx: parses the error envelope and throws `ApiError`.
 * - On success: returns parsed JSON validated against `schema`, the raw
 *   response when `raw: true`, or `undefined` when no schema is supplied
 *   (e.g. 204 No Content).
 *
 * Subtle correctness points worth preserving when extending this class:
 * - AbortError must propagate unchanged (so react-query / SWR cancellation
 *   semantics stay intact). Wrapping it as a network error breaks the
 *   abort contract.
 * - The error envelope parse is best-effort — non-JSON or unexpected
 *   shapes synthesise a generic `http_error` envelope. The HTTP status
 *   stays accurate either way.
 * - `attachAuth` runs once per request, before fetch, and its returned
 *   Request's headers replace the assembled ones. Auth providers must
 *   not throw — return the request unchanged if there's no token.
 */
export class HttpClient {
  private readonly baseUrl: string;
  private readonly defaultHeaders: Record<string, string>;
  private readonly fetchImpl: typeof fetch;
  private readonly attachAuth: ((request: Request) => Request) | undefined;

  constructor(config: HttpClientConfig) {
    this.baseUrl = config.baseUrl.replace(/\/+$/, '');
    this.defaultHeaders = { ...(config.defaultHeaders ?? {}) };
    this.fetchImpl = config.fetchImpl ?? globalThis.fetch.bind(globalThis);
    this.attachAuth = config.attachAuth;
  }

  async request<TSchema extends z.ZodTypeAny>(
    path: string,
    init: HttpRequestInit<TSchema> & { schema: TSchema },
  ): Promise<z.infer<TSchema>>;
  async request(path: string, init?: HttpRequestInit<undefined>): Promise<unknown>;
  async request(
    path: string,
    init: HttpRequestInit<z.ZodTypeAny | undefined> = {},
  ): Promise<unknown> {
    const url = joinUrl(this.baseUrl, path);
    const method = init.method ?? 'GET';
    const headers = new Headers();
    for (const [k, v] of Object.entries(this.defaultHeaders)) headers.set(k, v);
    for (const [k, v] of Object.entries(init.headers ?? {})) headers.set(k, v);

    let body: BodyInit | undefined;
    if (init.body !== undefined && init.body !== null) {
      if (init.bodyKind === 'text') {
        body = String(init.body);
        if (!headers.has('Content-Type')) {
          headers.set('Content-Type', init.contentType ?? 'text/plain');
        }
      } else {
        body = JSON.stringify(init.body);
        if (!headers.has('Content-Type')) {
          headers.set('Content-Type', 'application/json');
        }
      }
    }
    if (!headers.has('Accept') && init.bodyKind !== 'text') {
      headers.set('Accept', 'application/json');
    }

    let response: Response;
    try {
      let finalHeaders = headers;
      if (this.attachAuth) {
        const probe = new Request(url, { method, headers, body });
        const attached = this.attachAuth(probe);
        finalHeaders = new Headers(attached.headers);
      }
      response = await this.fetchImpl(url, {
        method,
        headers: finalHeaders,
        body,
        signal: init.signal,
      });
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') throw err;
      throw new ApiError(0, {
        error: {
          code: 'network_unreachable',
          message: err instanceof Error ? err.message : 'Network unreachable',
        },
      });
    }

    if (!response.ok) {
      const envelope = await parseErrorEnvelope(response);
      throw new ApiError(response.status, envelope);
    }

    if (init.raw) {
      const text = await response.text();
      const raw: RawHttpResponse = {
        status: response.status,
        headers: response.headers,
        text,
      };
      return raw;
    }

    if (response.status === 204) return undefined;

    const text = await response.text();
    if (!text) return undefined;

    let json: unknown;
    try {
      json = JSON.parse(text);
    } catch {
      throw new ApiParseError(`Response was not valid JSON`, []);
    }

    if (!init.schema) return json;
    const parsed = init.schema.safeParse(json);
    if (!parsed.success) {
      throw new ApiParseError('Response did not match expected schema', parsed.error.issues);
    }
    return parsed.data;
  }
}

/** Helper for composing query-string params, omitting `undefined` values. */
export function buildQuery(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined) continue;
    search.set(key, String(value));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
}
