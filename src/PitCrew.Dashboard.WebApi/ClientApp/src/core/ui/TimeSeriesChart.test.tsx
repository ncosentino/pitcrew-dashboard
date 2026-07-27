import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { TimeSeriesChart } from './TimeSeriesChart';

describe('TimeSeriesChart', () => {
  it('exposes every plotted measurement through an equivalent data table', () => {
    render(
      <TimeSeriesChart
        cadenceMilliseconds={null}
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
        headingLevel="h3"
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
        cadenceMilliseconds={null}
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
        headingLevel="h3"
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
        cadenceMilliseconds={null}
        description="Cumulative worker network counters."
        series={[
          {
            key: 'network-rx',
            label: 'Received',
            description: 'Cumulative bytes received.',
            points: [{ at: '2026-07-26T12:00:00+00:00', value: null }],
          },
        ]}
        headingLevel="h3"
        testId="network-chart"
        title="Network"
        unit="bytes"
      />,
    );

    expect(screen.getByText(/No measurement in this range was available/)).toBeInTheDocument();
  });

  it('plots a measured-zero series flat on the zero baseline', () => {
    const { container } = render(
      <TimeSeriesChart
        cadenceMilliseconds={null}
        description="Reported worker exits."
        series={[
          {
            key: 'exits',
            label: 'Reported exits',
            description: 'Workers carrying exit evidence.',
            points: [
              { at: '2026-07-26T12:00:00+00:00', value: 0 },
              { at: '2026-07-26T12:00:15+00:00', value: 0 },
            ],
          },
        ]}
        headingLevel="h3"
        testId="exits-chart"
        title="Exits"
        unit="count"
      />,
    );

    expect(container.querySelector('polyline')?.getAttribute('points')).toBe(
      '0.00,120.00 600.00,120.00',
    );
  });

  it('positions points by observation time so a real gap is not drawn as continuous data', () => {
    const { container } = render(
      <TimeSeriesChart
        cadenceMilliseconds={null}
        description="Accepted desired capacity."
        headingLevel="h3"
        series={[
          {
            key: 'desired',
            label: 'Desired slots',
            description: 'Requested slots.',
            points: [
              { at: '2026-07-26T12:00:00+00:00', value: 0 },
              { at: '2026-07-26T12:00:15+00:00', value: 0 },
              { at: '2026-07-26T13:00:00+00:00', value: 0 },
            ],
          },
        ]}
        testId="capacity-chart"
        title="Capacity"
        unit="count"
      />,
    );

    expect(container.querySelector('polyline')?.getAttribute('points')).toBe(
      '0.00,120.00 2.50,120.00 600.00,120.00',
    );
  });

  it('renders the measurement table inside a focusable labelled region', () => {
    render(
      <TimeSeriesChart
        cadenceMilliseconds={null}
        description="Accepted desired capacity."
        headingLevel="h4"
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

    const region = screen.getByRole('region', { name: 'Capacity measurements' });
    expect(region).toHaveAttribute('tabindex', '0');
    expect(screen.getByRole('heading', { level: 4, name: 'Capacity' })).toBeInTheDocument();
  });

  it('hides the decorative plot from assistive technology', () => {
    const { container } = render(
      <TimeSeriesChart
        cadenceMilliseconds={null}
        description="Accepted desired capacity."
        series={[
          {
            key: 'desired',
            label: 'Desired slots',
            description: 'Requested slots.',
            points: [{ at: '2026-07-26T12:00:00+00:00', value: 3 }],
          },
        ]}
        headingLevel="h3"
        testId="capacity-chart"
        title="Capacity"
        unit="count"
      />,
    );

    expect(container.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
  it('breaks the plotted line across a materially missing cadence gap', () => {
    const { container } = render(
      <TimeSeriesChart
        cadenceMilliseconds={15_000}
        description="Desired slots."
        headingLevel="h3"
        series={[
          {
            key: 'desired-slots',
            label: 'Desired slots',
            description: 'Requested slots.',
            points: [
              { at: '2026-07-26T12:00:00+00:00', value: 1 },
              { at: '2026-07-26T12:00:15+00:00', value: 2 },
              { at: '2026-07-26T13:00:00+00:00', value: 3 },
              { at: '2026-07-26T13:00:15+00:00', value: 4 },
            ],
          },
        ]}
        testId="gap-chart"
        title="Capacity"
        unit="count"
      />,
    );

    expect(container.querySelectorAll('polyline')).toHaveLength(2);
  });

  it('keeps one line when every point follows the rendered cadence', () => {
    const { container } = render(
      <TimeSeriesChart
        cadenceMilliseconds={3_600_000}
        description="Desired slot peaks."
        headingLevel="h3"
        series={[
          {
            key: 'desired-slots',
            label: 'Peak desired slots',
            description: 'Requested slots.',
            points: [
              { at: '2026-07-26T12:00:00+00:00', value: 1 },
              { at: '2026-07-26T13:00:00+00:00', value: 2 },
              { at: '2026-07-26T14:00:00+00:00', value: 3 },
            ],
          },
        ]}
        testId="cadence-chart"
        title="Capacity"
        unit="count"
      />,
    );

    expect(container.querySelectorAll('polyline')).toHaveLength(1);
  });
});
