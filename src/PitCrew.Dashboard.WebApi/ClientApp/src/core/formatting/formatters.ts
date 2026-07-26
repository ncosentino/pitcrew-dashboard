const byteUnits = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB'] as const;

/** Formats an offset timestamp for the operator's locale. */
export function formatTime(value: string | null): string {
  if (value === null) return 'Never';
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value));
}

/** Formats a byte count using binary units. */
export function formatBytes(value: number): string {
  if (value === 0) return '0 B';
  const unitIndex = Math.min(Math.floor(Math.log(value) / Math.log(1024)), byteUnits.length - 1);
  const unitValue = value / 1024 ** unitIndex;
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(unitValue)} ${byteUnits[unitIndex]}`;
}

/** Formats a CPU core count. */
export function formatCpuCores(value: number): string {
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value)} cores`;
}

/** Formats a process count. */
export function formatPids(value: number): string {
  return `${new Intl.NumberFormat(undefined).format(value)} PIDs`;
}

/** Formats a duration represented in whole seconds. */
export function formatSeconds(value: number): string {
  return `${new Intl.NumberFormat(undefined).format(value)} seconds`;
}
