import type { ReactNode } from 'react';

/** One labeled fact rendered in the identity or fences evidence grid. */
export interface ConfirmationSummaryFact {
  readonly label: string;
  readonly value: ReactNode;
  readonly testId?: string;
}

/** Props for the shared consequential-operation confirmation summary. */
export interface ConfirmationSummaryProps {
  /** What is being acted on: the exact node, profile, credential, or other target. */
  readonly identity: ReadonlyArray<ConfirmationSummaryFact>;
  /** The exact effect the action will have (DESIGN.md "The Confirm Consequence Rule"). */
  readonly effects: ReadonlyArray<ReactNode>;
  /** What the action explicitly will not do, scoping the consequence for the operator. */
  readonly prohibitedEffects?: ReadonlyArray<ReactNode>;
  /** Preconditions the operation is fenced to; a stale or mismatched fence should invalidate the confirmation. */
  readonly fences?: ReadonlyArray<ConfirmationSummaryFact>;
  /** An explicit operator acknowledgement gating the confirm action, when the effect needs one beyond reading it. */
  readonly acknowledgement?: {
    readonly label: ReactNode;
    readonly checked: boolean;
    readonly onCheckedChange: (checked: boolean) => void;
    readonly testId?: string;
  };
}

function FactRow({ label, value, testId }: ConfirmationSummaryFact) {
  return (
    <div className="bg-background px-3 py-2">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-sm" data-testid={testId}>
        {value}
      </dd>
    </div>
  );
}

function FactGrid({ facts }: { readonly facts: ReadonlyArray<ConfirmationSummaryFact> }) {
  return (
    <dl className="grid grid-cols-1 gap-px overflow-hidden rounded-md border bg-border sm:grid-cols-2">
      {facts.map((fact) => (
        <FactRow key={fact.label} {...fact} />
      ))}
    </dl>
  );
}

/**
 * Renders the shared consequential-operation confirmation contract: what is
 * being acted on, the exact effect, what will not happen, the evidence the
 * action is fenced to, and an optional explicit acknowledgement. Compose
 * this inside ConfirmActionDialog's `details` slot so the confirm action
 * itself keeps naming the operation and the cancel/confirm affordances.
 */
export function ConfirmationSummary({
  identity,
  effects,
  prohibitedEffects,
  fences,
  acknowledgement,
}: ConfirmationSummaryProps) {
  return (
    <div className="grid gap-3 text-sm">
      {identity.length > 0 ? <FactGrid facts={identity} /> : null}
      {fences && fences.length > 0 ? (
        <div className="grid gap-1">
          <p className="font-medium">Expected fences</p>
          <FactGrid facts={fences} />
        </div>
      ) : null}
      <div className="grid gap-1">
        <p className="font-medium">What will happen</p>
        {effects.map((effect, index) => (
          <p className="text-muted-foreground" key={index}>
            {effect}
          </p>
        ))}
        {prohibitedEffects && prohibitedEffects.length > 0 ? (
          <>
            <p className="font-medium">What will not happen</p>
            {prohibitedEffects.map((effect, index) => (
              <p className="text-muted-foreground" key={index}>
                {effect}
              </p>
            ))}
          </>
        ) : null}
      </div>
      {acknowledgement ? (
        <label className="flex items-start gap-2 text-sm">
          <input
            checked={acknowledgement.checked}
            className="mt-1"
            data-testid={acknowledgement.testId}
            onChange={(event) => acknowledgement.onCheckedChange(event.target.checked)}
            type="checkbox"
          />
          <span>{acknowledgement.label}</span>
        </label>
      ) : null}
    </div>
  );
}
