import { describe, it, expect, vi } from 'vitest';
import { z } from 'zod';
import {
  HttpClient,
  ApiError,
  ApiParseError,
  buildQuery,
  type RawHttpResponse,
} from './httpClient';

function makeFetch(
  responses: {
    status: number;
    body?: string;
    headers?: Record<string, string>;
    throwError?: Error;
  }[],
): typeof fetch {
  let i = 0;
  return vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => {
    const next = responses[i++];
    if (!next) throw new Error('no more responses');
    if (next.throwError) throw next.throwError;
    return new Response(next.body ?? null, {
      status: next.status,
      headers: next.headers,
    });
  }) as unknown as typeof fetch;
}

describe('HttpClient.request', () => {
  it('parses JSON validated against a zod schema and strips trailing slashes from baseUrl', async () => {
    const schema = z.object({ ok: z.literal(true) });
    const fetchImpl = makeFetch([{ status: 200, body: JSON.stringify({ ok: true }) }]);
    const client = new HttpClient({ baseUrl: 'http://example.com/', fetchImpl });
    const data = await client.request('/x', { schema });
    expect(data).toEqual({ ok: true });
  });

  it('throws ApiError with parsed envelope on non-2xx', async () => {
    const body = JSON.stringify({ error: { code: 'not_found', message: 'gone' } });
    const fetchImpl = makeFetch([
      { status: 404, body, headers: { 'Content-Type': 'application/json' } },
    ]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    await expect(client.request('/x')).rejects.toMatchObject({
      name: 'ApiError',
      status: 404,
      code: 'not_found',
    });
  });

  it('synthesises an error envelope when the body is missing or unparsable', async () => {
    const fetchImpl = makeFetch([{ status: 500, body: 'not json' }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    await expect(client.request('/x')).rejects.toMatchObject({
      name: 'ApiError',
      status: 500,
      code: 'http_error',
    });
  });

  it('wraps network failures as ApiError code=network_unreachable', async () => {
    const fetchImpl = makeFetch([{ status: 0, throwError: new Error('DNS broken') }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    await expect(client.request('/x')).rejects.toMatchObject({ code: 'network_unreachable' });
  });

  it('propagates AbortError unchanged', async () => {
    const abort = Object.assign(new Error('aborted'), { name: 'AbortError' });
    const fetchImpl = makeFetch([{ status: 0, throwError: abort }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    await expect(client.request('/x')).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('returns RawHttpResponse when raw=true', async () => {
    const fetchImpl = makeFetch([{ status: 200, body: 'hi', headers: { ETag: '"abc"' } }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    const raw = (await client.request('/x', { raw: true })) as RawHttpResponse;
    expect(raw.status).toBe(200);
    expect(raw.text).toBe('hi');
    expect(raw.headers.get('etag')).toBe('"abc"');
  });

  it('returns undefined for 204 No Content and for empty body', async () => {
    const fetchImpl = makeFetch([{ status: 204 }, { status: 200 }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    expect(await client.request('/x')).toBeUndefined();
    expect(await client.request('/y')).toBeUndefined();
  });

  it('throws ApiParseError when response body is not JSON', async () => {
    const fetchImpl = makeFetch([{ status: 200, body: '<<not json>>' }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    await expect(client.request('/x')).rejects.toBeInstanceOf(ApiParseError);
  });

  it('throws ApiParseError with schema mismatch', async () => {
    const schema = z.object({ ok: z.literal(true) });
    const fetchImpl = makeFetch([{ status: 200, body: JSON.stringify({ ok: false }) }]);
    const client = new HttpClient({ baseUrl: 'http://example.com', fetchImpl });
    await expect(client.request('/x', { schema })).rejects.toBeInstanceOf(ApiParseError);
  });

  it('sends JSON bodies with content-type and default headers', async () => {
    const spy = vi.fn(async (_url: RequestInfo | URL, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      expect(headers.get('Content-Type')).toBe('application/json');
      expect(headers.get('Accept')).toBe('application/json');
      expect(headers.get('X-App-Token')).toBe('secret');
      expect(init?.body).toBe(JSON.stringify({ k: 1 }));
      return new Response(JSON.stringify({ done: true }), { status: 200 });
    });
    const client = new HttpClient({
      baseUrl: 'http://example.com',
      defaultHeaders: { 'X-App-Token': 'secret' },
      fetchImpl: spy as unknown as typeof fetch,
    });
    await client.request('/x', { method: 'POST', body: { k: 1 } });
    expect(spy).toHaveBeenCalled();
  });

  it('passes the supplied AbortSignal through to fetch', async () => {
    const spy = vi.fn(async (_url: RequestInfo | URL, init?: RequestInit) => {
      expect(init?.signal).toBeInstanceOf(AbortSignal);
      return new Response(null, { status: 204 });
    });
    const client = new HttpClient({
      baseUrl: 'http://example.com',
      fetchImpl: spy as unknown as typeof fetch,
    });
    await client.request('/x', { signal: new AbortController().signal });
  });

  it('invokes attachAuth before fetch and uses the headers from the returned Request', async () => {
    const seenAuth: string[] = [];
    const spy = vi.fn(async (_url: RequestInfo | URL, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      const value = headers.get('Authorization');
      if (value !== null) seenAuth.push(value);
      return new Response(null, { status: 204 });
    });
    const attachAuth = vi.fn((request: Request) => {
      const headers = new Headers(request.headers);
      headers.set('Authorization', 'Bearer abc123');
      return new Request(request.url, {
        method: request.method,
        headers,
        body: request.method === 'GET' || request.method === 'HEAD' ? undefined : request.body,
      });
    });
    const client = new HttpClient({
      baseUrl: 'http://example.com',
      fetchImpl: spy as unknown as typeof fetch,
      attachAuth,
    });
    await client.request('/x');
    expect(attachAuth).toHaveBeenCalledTimes(1);
    expect(seenAuth).toEqual(['Bearer abc123']);
  });

  it('ApiError exposes the parsed envelope', () => {
    const e = new ApiError(409, {
      error: { code: 'already_resolved', message: 'done', detail: 1 },
    });
    expect(e.code).toBe('already_resolved');
    expect(e.detail).toBe(1);
    expect(e.envelope.error.message).toBe('done');
    expect(e.message).toBe('done');
  });

  it('ApiParseError carries zod issues', () => {
    const e = new ApiParseError('boom', []);
    expect(e.issues).toEqual([]);
    expect(e.name).toBe('ApiParseError');
  });
});

describe('buildQuery', () => {
  it('omits undefined values and prefixes ? when non-empty', () => {
    expect(buildQuery({ a: 1, b: undefined, c: 'x' })).toBe('?a=1&c=x');
    expect(buildQuery({})).toBe('');
    expect(buildQuery({ a: undefined })).toBe('');
    expect(buildQuery({ flag: true })).toBe('?flag=true');
  });
});
