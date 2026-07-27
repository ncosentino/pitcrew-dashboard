import { useId } from 'react';

import { formatBytes, formatCpuCores, formatTime } from '@/core/formatting/formatters';

/** Measurement unit used to format one plotted series. */
export type TimeSeriesUnit = 'count' | 'bytes' | 'cores' | 'pids';

/** One plotted point, where `null` means unavailable rather than a measured zero. */
export interface TimeSeriesPoint {
  readonly at: string;
  readonly value: number | null;
}

/** One named plotted series. */
export interface TimeSeriesDefinition {
  readonly key: string;
  readonly label: string;
  readonly description: string;
  readonly points: readonly TimeSeriesPoint[];
}

/** Heading element used for one chart title so the surrounding outline stays correct. */
export type TimeSeriesHeadingLevel = 'h3' | 'h4';

interface TimeSeriesChartProps {
  readonly title: string;
  readonly description: string;
  readonly unit: TimeSeriesUnit;
  readonly series: readonly TimeSeriesDefinition[];
  readonly headingLevel: TimeSeriesHeadingLevel;
  readonly testId: string;
}

const strokes = [
  'stroke-sky-600 dark:stroke-sky-400',
  'stroke-emerald-600 dark:stroke-emerald-400',
  'stroke-amber-600 dark:stroke-amber-400',
  'stroke-fuchsia-600 dark:stroke-fuchsia-400',
  'stroke-slate-500 dark:stroke-slate-300',
] as const;

const swatches = [
  'bg-sky-600 dark:bg-sky-400',
  'bg-emerald-600 dark:bg-emerald-400',
  'bg-amber-600 dark:bg-amber-400',
  'bg-fuchsia-600 dark:bg-fuchsia-400',
  'bg-slate-500 dark:bg-slate-300',
] as const;

const viewWidth = 600;
const viewHeight = 120;

/** Formats one measurement, keeping an unavailable observation distinct from a measured zero. */
function formatMeasurement(value: number | null, unit: TimeSeriesUnit): string {
  if (value == null) return 'Unavailable';
  if (unit === 'bytes') return formatBytes(value);
  if (unit === 'cores') return formatCpuCores(value);
  if (unit === 'pids') return formatPidCount(value);
  return new Intl.NumberFormat(undefined).format(value);
}

function formatPidCount(value: number): string {
  return `${new Intl.NumberFormat(undefined).format(value)} PIDs`;
}

function measuredValues(series: readonly TimeSeriesDefinition[]): number[] {
  return series.flatMap((entry) =>
    entry.points.flatMap((point) => (point.value == null ? [] : [point.value])),
  );
}

function orderedTimestamps(series: readonly TimeSeriesDefinition[]): string[] {
  const seen = new Set<string>();
  for (const entry of series) {
    for (const point of entry.points) {
      seen.add(point.at);
    }
  }
  return [...seen].sort((left, right) => Date.parse(left) - Date.parse(right));
}

/**
 * Plots one series with time-proportional horizontal positions.
 *
 * Points are positioned by their observation time rather than by their index, so a real gap in
 * retained observations is drawn as a gap instead of as evenly spaced continuous data.
 */
function buildSegments(
  entry: TimeSeriesDefinition,
  timestamps: readonly string[],
  minimum: number,
  maximum: number,
): string[] {
  const span = maximum - minimum === 0 ? 1 : maximum - minimum;
  const times = timestamps.map((value) => Date.parse(value));
  const earliest = times.length === 0 ? 0 : Math.min(...times);
  const latest = times.length === 0 ? 0 : Math.max(...times);
  const timeSpan = latest - earliest;
  const known = new Set(timestamps);
  const segments: string[] = [];
  let current: string[] = [];
  for (const point of entry.points) {
    const at = Date.parse(point.at);
    if (point.value == null || !known.has(point.at) || Number.isNaN(at)) {
      if (current.length > 0) segments.push(current.join(' '));
      current = [];
      continue;
    }
    const x = timeSpan === 0 ? viewWidth / 2 : ((at - earliest) / timeSpan) * viewWidth;
    const y = viewHeight - ((point.value - minimum) / span) * viewHeight;
    current.push(`${x.toFixed(2)},${y.toFixed(2)}`);
  }
  if (current.length > 0) segments.push(current.join(' '));
  return segments;
}

/**
 * Renders one accessible time-range chart.
 *
 * The plot is decorative for assistive technology and is always paired with an equivalent data
 * table, so no measurement is available only through colour or shape. Unavailable observations
 * break the plotted line instead of being drawn as zero.
 */
export function TimeSeriesChart({
  title,
  description,
  unit,
  series,
  headingLevel,
  testId,
}: TimeSeriesChartProps) {
  const tableId = useId();
  const Heading = headingLevel;
  const timestamps = orderedTimestamps(series);
  const measured = measuredValues(series);
  const minimum = measured.length === 0 ? 0 : Math.min(...measured, 0);
  const maximum = measured.length === 0 ? 1 : Math.max(...measured);

  return (
    <section className="space-y-2" data-testid={testId}>
      <div>
        <Heading className="text-sm font-semibold">{title}</Heading>
        <p className="text-xs text-muted-foreground">{description}</p>
      </div>
      {measured.length === 0 ? (
        <p className="rounded border border-dashed px-3 py-4 text-xs text-muted-foreground">
          No measurement in this range was available for {title.toLowerCase()}. The manager
          published no value rather than reporting zero.
        </p>
      ) : (
        <>
          <svg
            aria-hidden="true"
            className="h-28 w-full"
            focusable="false"
            preserveAspectRatio="none"
            viewBox={`0 0 ${viewWidth} ${viewHeight}`}
          >
            {series.map((entry, index) =>
              buildSegments(entry, timestamps, minimum, maximum).map((segment, segmentIndex) => (
                <polyline
                  className={strokes[index % strokes.length]}
                  fill="none"
                  key={`${entry.key}-${segmentIndex}`}
                  points={segment}
                  strokeWidth={2}
                  vectorEffect="non-scaling-stroke"
                />
              )),
            )}
          </svg>
          <ul className="flex flex-wrap gap-3 text-xs text-muted-foreground">
            {series.map((entry, index) => (
              <li className="flex items-center gap-1.5" key={entry.key}>
                <span
                  aria-hidden="true"
                  className={`inline-block h-2 w-2 rounded-full ${swatches[index % swatches.length]}`}
                />
                {entry.label}
              </li>
            ))}
          </ul>
        </>
      )}
      <details className="text-xs">
        <summary className="cursor-pointer text-muted-foreground">
          Show {title.toLowerCase()} measurements as a table
        </summary>
        <div
          aria-label={`${title} measurements`}
          className="mt-2 max-h-64 overflow-auto focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-600"
          role="region"
          tabIndex={0}
        >
          <table className="w-full text-left" id={tableId}>
            <caption className="sr-only">
              {title}. {description}
            </caption>
            <thead className="text-muted-foreground">
              <tr>
                <th scope="col">Observed at</th>
                {series.map((entry) => (
                  <th key={entry.key} scope="col" title={entry.description}>
                    {entry.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {timestamps.map((timestamp) => (
                <tr key={timestamp}>
                  <th className="font-normal" scope="row">
                    {formatTime(timestamp)}
                  </th>
                  {series.map((entry) => (
                    <td key={entry.key}>
                      {formatMeasurement(
                        entry.points.find((point) => point.at === timestamp)?.value ?? null,
                        unit,
                      )}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </section>
  );
}
