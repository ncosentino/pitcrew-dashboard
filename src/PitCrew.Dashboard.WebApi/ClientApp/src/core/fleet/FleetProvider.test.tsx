import { StrictMode } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { FleetProvider } from './FleetProvider';
import { useFleet } from './useFleet';

function fleetResponse(generatedAt: string) {
  return new Response(JSON.stringify({ generatedAt, nodes: [] }), {
    headers: { 'Content-Type': 'application/json' },
  });
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

function FleetProbe() {
  const { fleet, error, isLoading, refreshNow } = useFleet();
  return (
    <>
      <div>{isLoading ? 'loading' : 'loaded'}</div>
      <div>{error ?? 'no error'}</div>
      <div>{fleet?.generatedAt ?? 'no fleet'}</div>
      <button type="button" onClick={() => void refreshNow()}>
        Refresh
      </button>
    </>
  );
}

async function flushInitialLoad() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('FleetProvider', () => {
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('keeps one polling loop under Strict Mode', async () => {
    vi.useFakeTimers();
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(fleetResponse('2026-07-26T02:00:00+00:00'));

    render(
      <StrictMode>
        <FleetProvider tenantId="local">
          <FleetProbe />
        </FleetProvider>
      </StrictMode>,
    );
    await flushInitialLoad();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('aborts superseded requests during manual and scheduled refreshes', async () => {
    vi.useFakeTimers();
    const requests: AbortSignal[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => {
      requests.push(init?.signal as AbortSignal);
      return new Promise<Response>(() => undefined);
    });
    render(
      <FleetProvider tenantId="local">
        <FleetProbe />
      </FleetProvider>,
    );
    await flushInitialLoad();

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(requests).toHaveLength(2);
    expect(requests[0].aborted).toBe(true);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(requests).toHaveLength(3);
    expect(requests[1].aborted).toBe(true);
  });

  it('aborts a tenant request and suppresses its late response after switching', async () => {
    vi.useFakeTimers();
    const local = deferred<Response>();
    const signals: AbortSignal[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      signals.push(init?.signal as AbortSignal);
      return String(input).includes('/local/')
        ? local.promise
        : Promise.resolve(fleetResponse('2026-07-26T02:01:00+00:00'));
    });
    const { rerender } = render(
      <FleetProvider tenantId="local">
        <FleetProbe />
      </FleetProvider>,
    );
    await flushInitialLoad();

    rerender(
      <FleetProvider tenantId="remote">
        <FleetProbe />
      </FleetProvider>,
    );
    expect(screen.getByText('no fleet')).toBeInTheDocument();
    expect(signals[0].aborted).toBe(true);
    await flushInitialLoad();
    expect(screen.getByText('2026-07-26T02:01:00+00:00')).toBeInTheDocument();

    local.resolve(fleetResponse('2026-07-26T02:00:00+00:00'));
    await act(async () => {
      await Promise.resolve();
    });
    expect(screen.queryByText('2026-07-26T02:00:00+00:00')).not.toBeInTheDocument();
  });

  it('stops polling and aborts the active request when unmounted', async () => {
    vi.useFakeTimers();
    const signals: AbortSignal[] = [];
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => {
      signals.push(init?.signal as AbortSignal);
      return new Promise<Response>(() => undefined);
    });
    const { unmount } = render(
      <FleetProvider tenantId="local">
        <FleetProbe />
      </FleetProvider>,
    );
    await flushInitialLoad();

    unmount();
    expect(signals[0].aborted).toBe(true);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not start the initial request after unmounting before its microtask', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    const { unmount } = render(
      <FleetProvider tenantId="local">
        <FleetProbe />
      </FleetProvider>,
    );

    unmount();
    await act(async () => {
      await Promise.resolve();
    });

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('keeps stale data on failure and clears the error after recovery', async () => {
    vi.useFakeTimers();
    let request = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => {
      request++;
      if (request === 2) return Promise.reject(new Error('temporary failure'));
      return Promise.resolve(
        fleetResponse(request === 1 ? '2026-07-26T02:00:00+00:00' : '2026-07-26T02:02:00+00:00'),
      );
    });
    render(
      <FleetProvider tenantId="local">
        <FleetProbe />
      </FleetProvider>,
    );
    await flushInitialLoad();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(screen.getByText('temporary failure')).toBeInTheDocument();
    expect(screen.getByText('2026-07-26T02:00:00+00:00')).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(screen.getByText('no error')).toBeInTheDocument();
    expect(screen.getByText('2026-07-26T02:02:00+00:00')).toBeInTheDocument();
  });
});
