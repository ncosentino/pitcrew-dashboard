import type { HistoryCapabilities, HistoryResolution } from './historyApi';

/** One selectable history range built from server-advertised capabilities. */
export interface HistoryPreset {
  readonly key: string;
  readonly hours: number;
  readonly label: string;
  readonly resolution: HistoryResolution;
  readonly pointLimit: number | null;
  readonly eventLimit: number | null;
  readonly diagnosticLimit: number | null;
  readonly description: string;
}

const secondsPerHour = 3600;
const rawCandidateHours = [4, 12, 24] as const;
const hourlyCandidateHours = [24, 168, 720] as const;
const preferredEventLimit = 200;
const preferredDiagnosticLimit = 200;

function omitWhenDefault(desired: number, maximum: number): number | null {
  return desired >= maximum ? null : desired;
}

function describeHours(hours: number): string {
  if (hours % 24 === 0 && hours >= 24) {
    const days = hours / 24;
    return days === 1 ? '24 hours' : `${days} days`;
  }
  return hours === 1 ? 'hour' : `${hours} hours`;
}

function buildRawPreset(capabilities: HistoryCapabilities): HistoryPreset {
  const cadenceSeconds = Math.max(1, capabilities.expectedRawCadenceSeconds);
  const budgetHours = Math.max(
    1,
    Math.floor((capabilities.maximumPoints * cadenceSeconds) / secondsPerHour),
  );
  const hours = Math.max(
    1,
    Math.min(
      capabilities.maximumRangeHours,
      rawCandidateHours.reduce(
        (best, candidate) => (candidate <= budgetHours ? candidate : best),
        1,
      ),
    ),
  );
  const points = Math.max(
    1,
    Math.min(capabilities.maximumPoints, Math.ceil((hours * secondsPerHour) / cadenceSeconds)),
  );
  return {
    key: `raw-${hours}`,
    hours,
    label: `Last ${describeHours(hours)} (every observation)`,
    resolution: 'raw',
    pointLimit: omitWhenDefault(points, capabilities.maximumPoints),
    eventLimit: omitWhenDefault(
      Math.min(preferredEventLimit, capabilities.maximumEvents),
      capabilities.maximumEvents,
    ),
    diagnosticLimit: omitWhenDefault(
      Math.min(preferredDiagnosticLimit, capabilities.maximumDiagnostics),
      capabilities.maximumDiagnostics,
    ),
    description: `Showing up to ${points} retained per-observation samples per profile. At the expected ${cadenceSeconds}-second heartbeat that covers roughly the last ${describeHours(hours)}; longer per-observation ranges cannot be shown truthfully because the response is capped.`,
  };
}

function buildHourlyPreset(hours: number, capabilities: HistoryCapabilities): HistoryPreset {
  const points = Math.max(1, Math.min(capabilities.maximumPoints, hours));
  return {
    key: `hourly-${hours}`,
    hours,
    label: `Last ${describeHours(hours)} (hourly peaks)`,
    resolution: 'hourly',
    pointLimit: omitWhenDefault(points, capabilities.maximumPoints),
    eventLimit: omitWhenDefault(
      Math.min(preferredEventLimit, capabilities.maximumEvents),
      capabilities.maximumEvents,
    ),
    diagnosticLimit: omitWhenDefault(
      Math.min(preferredDiagnosticLimit, capabilities.maximumDiagnostics),
      capabilities.maximumDiagnostics,
    ),
    description:
      'Showing deterministic hourly peaks aligned to whole UTC hours. Partial hours at either edge of the range are excluded.',
  };
}

/**
 * Designs the selectable history ranges from the limits the server advertises.
 *
 * Presets are never hard-coded because a server may advertise a maximum range shorter than any
 * fixed preset, or lower point, event, and diagnostic ceilings than a fixed preset would request.
 * Every returned preset therefore fits inside the advertised ceilings, and an optional cap is
 * omitted whenever the server default already matches it. An hourly preset is only offered when the
 * advertised range can still contain a whole UTC hour after inward alignment, so no preset can
 * produce a rejected request.
 */
export function buildHistoryPresets(capabilities: HistoryCapabilities): readonly HistoryPreset[] {
  const supportsRaw = capabilities.resolutions.includes('raw');
  const supportsHourly = capabilities.resolutions.includes('hourly');
  const presets: HistoryPreset[] = [];
  if (supportsRaw) {
    presets.push(buildRawPreset(capabilities));
  }
  if (supportsHourly) {
    for (const hours of hourlyCandidateHours) {
      if (hours <= capabilities.maximumRangeHours && hours <= capabilities.maximumPoints) {
        presets.push(buildHourlyPreset(hours, capabilities));
      }
    }
    if (presets.length === 0) {
      const hours = Math.min(capabilities.maximumRangeHours, capabilities.maximumPoints);
      if (hours >= 2) {
        presets.push(buildHourlyPreset(hours, capabilities));
      }
    }
  }

  return presets;
}
