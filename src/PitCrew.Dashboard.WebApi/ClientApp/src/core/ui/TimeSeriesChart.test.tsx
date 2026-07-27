import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { TimeSeriesChart } from './TimeSeriesChart';

describe('TimeSeriesChart', () => {
  it('exposes every plotted measurement through an equivalent data table', () => {
    render(
      <TimeSeriesChart
        description="Manager and worker memory."
        series={[
          {
            key: 'manager-memory',
            label: 'Manager memory',
            description: 'Working set of the manager process.',
            points: [
              { at: '2026-07-26T12:00:00+00:00', value: 1_048_576 },
              { at: '2026-07-26T12:00:15+00:00', value: 2_097_152 },
            ],
          },
        ]}
        testId="memory-chart"
        title="Memory"
        unit="bytes"
      />,
    );

    const table = screen.getByRole('table', { name: /Memory\. Manager and worker memory\./ });
    expect(within(table).getByText('1 MiB')).toBeInTheDocument();
    expect(within(table).getByText('2 MiB')).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Manager memory' })).toBeInTheDocument();
  });

  it('renders an unavailable measurement as unavailable rather than as zero', () => {
    render(
      <TimeSeriesChart
        description="Control-plane connected runners."
        series={[
          {
            key: 'eligible',
            label: 'Connected runners',
            description: 'Runners GitHub reported as connected.',
            points: [
              { at: '2026-07-26T12:00:00+00:00', value: null },
              { at: '2026-07-26T12:00:15+00:00', value: 0 },
            ],
          },
        ]}
        testId="counts-chart"
        title="Runners"
        unit="count"
      />,
    );

    const table = screen.getByRole('table', { name: /Runners\./ });
    expect(within(table).getByText('Unavailable')).toBeInTheDocument();
    expect(within(table).getByText('0')).toBeInTheDocument();
  });

  it('states that no measurement was available instead of plotting an empty range', () => {
    render(
      <TimeSeriesChart
        description="Cumulative worker network counters."
        series={[
          {
            key: 'network-rx',
            label: 'Received',
            description: 'Cumulative bytes received.',
            points: [{ at: '2026-07-26T12:00:00+00:00', value: null }],
          },
        ]}
        testId="network-chart"
        title="Network"
        unit="bytes"
      />,
    );

    expect(screen.getByText(/No measurement in this range was available/)).toBeInTheDocument();
  });

  it('hides the decorative plot from assistive technology', () => {
    const { container } = render(
      <TimeSeriesChart
        description="Accepted desired capacity."
        series={[
          {
            key: 'desired',
            label: 'Desired slots',
            description: 'Requested slots.',
            points: [{ at: '2026-07-26T12:00:00+00:00', value: 3 }],
          },
        ]}
        testId="capacity-chart"
        title="Capacity"
        unit="count"
      />,
    );

    expect(container.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
});
