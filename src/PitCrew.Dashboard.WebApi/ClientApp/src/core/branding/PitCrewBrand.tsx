import { cn } from '@/lib/utils';

export type PitCrewBrandVariant = 'compact' | 'hero';

export interface PitCrewBrandProps {
  readonly variant: PitCrewBrandVariant;
}

/** Renders the canonical PitCrew artwork and dashboard product lockup. */
export function PitCrewBrand({ variant }: PitCrewBrandProps) {
  if (variant === 'hero') {
    return (
      <div className="flex flex-col items-center gap-2 text-center">
        <img className="size-40 object-contain sm:size-48" src="/pitcrew-logo.png" alt="PitCrew" />
        <div className="text-sm font-bold tracking-[0.32em] text-[var(--brand-teal)] uppercase">
          Dashboard
        </div>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-3">
      <img
        className="size-14 rounded-2xl object-contain shadow-sm"
        src="/pitcrew-favicon.png"
        alt=""
      />
      <div className="leading-none">
        <div
          className={cn(
            'text-xl font-black tracking-tight',
            'text-[var(--brand-navy)] dark:text-white',
          )}
        >
          Pit<span className="text-[var(--brand-orange)]">Crew</span>
        </div>
        <div className="mt-1 text-xs font-bold tracking-[0.24em] text-[var(--brand-teal)] uppercase">
          Dashboard
        </div>
      </div>
    </div>
  );
}
