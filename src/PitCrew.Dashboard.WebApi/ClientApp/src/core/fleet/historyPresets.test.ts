import { describe, expect, it } from 'vitest';

import type { HistoryCapabilities } from './historyApi';
import { buildHistoryPresets } from './historyPresets';

function capabilities(overrides: Partial<HistoryCapabilities> = {}): HistoryCapabilities {
  return {
    defaultRangeHours: 4,
    maximumRangeHours: 720,
    resolutions: ['raw', 'hourly'],
    maximumPoints: 1000,
    maximumEvents: 200,
    maximumDiagnostics: 200,
    nodePointLimit: 5000,
    nodeEventLimit: 1000,
    nodeDiagnosticLimit: 1000,
    expectedRawCadenceSeconds: 15,
    sampleRetentionHours: 336,
    rollupRetentionHours: 2160,
    ...overrides,
  };
}

describe('buildHistoryPresets', () => {
  it('keeps every preset inside the advertised range and point ceilings', () => {
    const advertised = capabilities();
    const presets = buildHistoryPresets(advertised);

    expect(presets.length).toBeGreaterThan(0);
    for (const preset of presets) {
      expect(preset.hours).toBeLessThanOrEqual(advertised.maximumRangeHours);
      expect(preset.pointLimit ?? advertised.maximumPoints).toBeLessThanOrEqual(
        advertised.maximumPoints,
      );
      expect(preset.eventLimit ?? advertised.maximumEvents).toBeLessThanOrEqual(
        advertised.maximumEvents,
      );
      expect(preset.diagnosticLimit ?? advertised.maximumDiagnostics).toBeLessThanOrEqual(
        advertised.maximumDiagnostics,
      );
    }
  });

  it('omits an optional cap when the server default already matches it', () => {
    const presets = buildHistoryPresets(capabilities({ maximumEvents: 200 }));

    expect(presets[0].eventLimit).toBeNull();
    expect(presets[0].diagnosticLimit).toBeNull();
  });

  it('requests fewer than the ceiling when the preferred cap is smaller', () => {
    const presets = buildHistoryPresets(
      capabilities({ maximumEvents: 5000, maximumDiagnostics: 5000 }),
    );

    expect(presets[0].eventLimit).toBe(200);
    expect(presets[0].diagnosticLimit).toBe(200);
  });

  it('never asks for more than a lower advertised event or diagnostic ceiling', () => {
    const advertised = capabilities({ maximumEvents: 20, maximumDiagnostics: 20 });
    const presets = buildHistoryPresets(advertised);

    for (const preset of presets) {
      expect(preset.eventLimit ?? advertised.maximumEvents).toBeLessThanOrEqual(20);
      expect(preset.diagnosticLimit ?? advertised.maximumDiagnostics).toBeLessThanOrEqual(20);
    }
  });

  it('still offers a preset when the advertised maximum range is under four hours', () => {
    const advertised = capabilities({ maximumRangeHours: 2, maximumPoints: 120 });
    const presets = buildHistoryPresets(advertised);

    expect(presets.length).toBeGreaterThan(0);
    expect(presets[0].resolution).toBe('raw');
    expect(presets[0].hours).toBeLessThanOrEqual(2);
    expect(presets.every((preset) => preset.hours <= 2)).toBe(true);
  });

  it('never offers an hourly preset that cannot contain a whole aligned UTC hour', () => {
    const presets = buildHistoryPresets(capabilities({ maximumRangeHours: 1 }));

    expect(presets.every((preset) => preset.resolution === 'raw')).toBe(true);
    expect(presets.length).toBeGreaterThan(0);
  });

  it('never offers an hourly preset with more buckets than the point ceiling', () => {
    const advertised = capabilities({ maximumPoints: 30 });
    const presets = buildHistoryPresets(advertised);

    expect(presets.length).toBeGreaterThan(0);
    for (const preset of presets.filter((candidate) => candidate.resolution === 'hourly')) {
      expect(preset.hours).toBeLessThanOrEqual(advertised.maximumPoints);
    }
  });
});
