import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Converts an `oklch(L% C H)` string to WCAG relative luminance without a
 * color-math dependency, using Björn Ottosson's published OKLab/OKLCH to
 * linear-sRGB matrices. The intermediate r/g/b values are already linear
 * light (pre-gamma), which is exactly the representation the WCAG relative
 * luminance formula's 0.2126/0.7152/0.0722 weights expect, so no separate
 * sRGB-to-linear step is needed.
 */
function relativeLuminanceFromOklch(oklch: string): number {
  const match = /oklch\(\s*([\d.]+)%\s+([\d.]+)\s+([\d.]+)\s*\)/.exec(oklch);
  if (!match) {
    throw new Error(`Not a recognized oklch() token: ${oklch}`);
  }
  const l = Number(match[1]) / 100;
  const c = Number(match[2]);
  const hueDegrees = Number(match[3]);
  const hueRadians = (hueDegrees * Math.PI) / 180;
  const a = c * Math.cos(hueRadians);
  const b = c * Math.sin(hueRadians);

  const lPrime = l + 0.3963377774 * a + 0.2158037573 * b;
  const mPrime = l - 0.1055613458 * a - 0.0638541728 * b;
  const sPrime = l - 0.0894841775 * a - 1.291485548 * b;

  const lCubed = lPrime ** 3;
  const mCubed = mPrime ** 3;
  const sCubed = sPrime ** 3;

  const linearR = 4.0767416621 * lCubed - 3.3077115913 * mCubed + 0.2309699292 * sCubed;
  const linearG = -1.2684380046 * lCubed + 2.6097574011 * mCubed - 0.3413193965 * sCubed;
  const linearB = -0.0041960863 * lCubed - 0.7034186147 * mCubed + 1.707614701 * sCubed;

  const clamp = (value: number) => Math.min(Math.max(value, 0), 1);
  return 0.2126 * clamp(linearR) + 0.7152 * clamp(linearG) + 0.0722 * clamp(linearB);
}

/**
 * Converts a `#rgb` or `#rrggbb` hex color to WCAG relative luminance,
 * applying the standard sRGB-to-linear gamma correction before the
 * 0.2126/0.7152/0.0722 luminance weights.
 */
function relativeLuminanceFromHex(hex: string): number {
  const match = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.exec(hex.trim());
  if (!match) {
    throw new Error(`Not a recognized #hex color token: ${hex}`);
  }
  const digits = match[1];
  const expanded =
    digits.length === 3
      ? digits
          .split('')
          .map((digit) => digit + digit)
          .join('')
      : digits;

  const toChannel = (start: number) => parseInt(expanded.slice(start, start + 2), 16) / 255;
  const toLinear = (channel: number) =>
    channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;

  const r = toLinear(toChannel(0));
  const g = toLinear(toChannel(2));
  const b = toLinear(toChannel(4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/**
 * Dispatches to the oklch or hex luminance parser based on the token's
 * format, since design tokens in globals.css mix both representations.
 */
function relativeLuminance(colorToken: string): number {
  if (colorToken.startsWith('#')) {
    return relativeLuminanceFromHex(colorToken);
  }
  if (colorToken.startsWith('oklch(')) {
    return relativeLuminanceFromOklch(colorToken);
  }
  throw new Error(`Unsupported color token format: ${colorToken}`);
}

function contrastRatio(colorA: string, colorB: string): number {
  const luminanceA = relativeLuminance(colorA);
  const luminanceB = relativeLuminance(colorB);
  const lighter = Math.max(luminanceA, luminanceB);
  const darker = Math.min(luminanceA, luminanceB);
  return (lighter + 0.05) / (darker + 0.05);
}

function extractDeclaration(css: string, blockSelector: string, property: string): string {
  const blockStart = css.indexOf(blockSelector);
  if (blockStart === -1) {
    throw new Error(`Could not find "${blockSelector}" block in globals.css`);
  }
  const blockEnd = css.indexOf('}', blockStart);
  const block = css.slice(blockStart, blockEnd);
  const declaration = new RegExp(`${property}:\\s*([^;]+);`).exec(block);
  if (!declaration) {
    throw new Error(`Could not find "${property}" in the "${blockSelector}" block`);
  }
  return declaration[1].trim();
}

const globalsCssPath = join(__dirname, 'globals.css');
const globalsCss = readFileSync(globalsCssPath, 'utf-8');

describe('status token contrast', () => {
  it.each([
    ['positive', '--status-positive', '--status-positive-foreground'],
    ['caution', '--status-caution', '--status-caution-foreground'],
    ['critical', '--status-critical', '--status-critical-foreground'],
  ])('meets AA (>= 4.5:1) for the light-theme %s status chip', (_name, bgVar, fgVar) => {
    const background = extractDeclaration(globalsCss, ':root {', bgVar);
    const foreground = extractDeclaration(globalsCss, ':root {', fgVar);

    expect(contrastRatio(background, foreground)).toBeGreaterThanOrEqual(4.5);
  });

  it.each([
    ['positive', '--status-positive', '--status-positive-foreground'],
    ['caution', '--status-caution', '--status-caution-foreground'],
    ['critical', '--status-critical', '--status-critical-foreground'],
  ])('meets AA (>= 4.5:1) for the dedicated dark-theme %s status chip', (_name, bgVar, fgVar) => {
    const background = extractDeclaration(globalsCss, '.dark {', bgVar);
    const foreground = extractDeclaration(globalsCss, '.dark {', fgVar);

    expect(contrastRatio(background, foreground)).toBeGreaterThanOrEqual(4.5);
  });

  it('uses distinct dark-theme status backgrounds rather than reusing the light-theme values', () => {
    const lightPositiveBg = extractDeclaration(globalsCss, ':root {', '--status-positive');
    const darkPositiveBg = extractDeclaration(globalsCss, '.dark {', '--status-positive');
    const lightCautionBg = extractDeclaration(globalsCss, ':root {', '--status-caution');
    const darkCautionBg = extractDeclaration(globalsCss, '.dark {', '--status-caution');
    const lightCriticalBg = extractDeclaration(globalsCss, ':root {', '--status-critical');
    const darkCriticalBg = extractDeclaration(globalsCss, '.dark {', '--status-critical');

    expect(darkPositiveBg).not.toBe(lightPositiveBg);
    expect(darkCautionBg).not.toBe(lightCautionBg);
    expect(darkCriticalBg).not.toBe(lightCriticalBg);
  });
});

describe('hex relative luminance parsing', () => {
  it('treats pure white as the maximum relative luminance of 1', () => {
    expect(relativeLuminanceFromHex('#ffffff')).toBeCloseTo(1, 10);
  });

  it('treats pure black as the minimum relative luminance of 0', () => {
    expect(relativeLuminanceFromHex('#000000')).toBeCloseTo(0, 10);
  });

  it('expands 3-digit shorthand to the same value as its 6-digit equivalent', () => {
    expect(relativeLuminanceFromHex('#07f')).toBeCloseTo(relativeLuminanceFromHex('#0077ff'), 10);
  });

  it('is case-insensitive', () => {
    expect(relativeLuminanceFromHex('#071825')).toBeCloseTo(
      relativeLuminanceFromHex('#071825'.toUpperCase()),
      10,
    );
  });

  it('matches a known reference luminance for the brand navy token', () => {
    // Cross-checked against an independent sRGB relative-luminance
    // calculation for #071825 (r=7, g=24, b=37).
    expect(relativeLuminanceFromHex('#071825')).toBeCloseTo(0.0083201, 6);
  });

  it('rejects malformed hex tokens instead of silently misparsing them', () => {
    expect(() => relativeLuminanceFromHex('071825')).toThrow();
    expect(() => relativeLuminanceFromHex('#12345')).toThrow();
    expect(() => relativeLuminanceFromHex('#gggggg')).toThrow();
    expect(() => relativeLuminanceFromHex('rgb(7, 24, 37)')).toThrow();
  });
});

describe('primary action token contrast', () => {
  it.each([
    ['light', ':root {'],
    ['dark', '.dark {'],
  ])('meets AA (>= 4.5:1) for --primary/--primary-foreground in the %s theme', (_name, block) => {
    const background = extractDeclaration(globalsCss, block, '--primary');
    const foreground = extractDeclaration(globalsCss, block, '--primary-foreground');

    expect(contrastRatio(background, foreground)).toBeGreaterThanOrEqual(4.5);
  });
});

describe('link token contrast against actual surfaces', () => {
  it.each([
    ['light', ':root {', '--background'],
    ['light', ':root {', '--card'],
    ['dark', '.dark {', '--background'],
    ['dark', '.dark {', '--card'],
  ])('meets AA (>= 4.5:1) for --link against %s-theme %s', (_name, block, surfaceVar) => {
    const link = extractDeclaration(globalsCss, block, '--link');
    const surface = extractDeclaration(globalsCss, block, surfaceVar);

    expect(contrastRatio(link, surface)).toBeGreaterThanOrEqual(4.5);
  });
});
