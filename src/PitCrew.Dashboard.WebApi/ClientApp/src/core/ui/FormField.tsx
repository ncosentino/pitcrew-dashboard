import {
  cloneElement,
  isValidElement,
  useId,
  type AriaAttributes,
  type ReactElement,
  type ReactNode,
} from 'react';

/** The subset of a form control's props FormField reads and augments. */
export interface FormControlProps {
  readonly id?: string;
  readonly 'aria-describedby'?: string;
  readonly 'aria-invalid'?: AriaAttributes['aria-invalid'];
}

/** Props for the shared labeled form field wrapper. */
export interface FormFieldProps {
  /** Visible label text; DESIGN.md requires visible labels rather than placeholder-only text. */
  readonly label: string;
  /** The input, select, or other form control this field labels. */
  readonly children: ReactElement<FormControlProps>;
  /** Optional supporting or format-hint text rendered under the control. */
  readonly hint?: ReactNode;
  /** Validation error text; when present, is announced and marks the field invalid. */
  readonly error?: string;
}

/**
 * Wraps one labeled form control with the field-label typography and
 * spacing DESIGN.md's Inputs / Fields section specifies. The label
 * associates with the control through `htmlFor`/`id` — rather than by
 * wrapping the control — so hint and error text can sit alongside the
 * control without being folded into its accessible *name* (the HTML label
 * accname algorithm includes all of a wrapping label's text, which would
 * otherwise announce the hint and error as part of the field's name
 * instead of its description). Hint and error text get their own
 * generated IDs and are wired to the control through `aria-describedby`
 * (preserving any `aria-describedby` the caller already set on the
 * control), and an error additionally sets `aria-invalid="true"` on the
 * control.
 */
export function FormField({ label, children, hint, error }: FormFieldProps) {
  const generatedControlId = useId();
  const hintId = useId();
  const errorId = useId();
  const existingDescribedBy = isValidElement(children)
    ? (children.props['aria-describedby'] ?? null)
    : null;
  const controlId =
    (isValidElement(children) ? children.props.id : undefined) ?? generatedControlId;

  const describedBy =
    [hint ? hintId : null, error ? errorId : null, existingDescribedBy]
      .filter((value): value is string => Boolean(value))
      .join(' ') || undefined;

  const control = isValidElement(children)
    ? cloneElement(children, {
        id: controlId,
        'aria-describedby': describedBy,
        'aria-invalid': error ? true : children.props['aria-invalid'],
      })
    : children;

  return (
    <div className="grid gap-1">
      <label className="text-sm font-medium" htmlFor={controlId}>
        {label}
      </label>
      {control}
      {hint ? (
        <span className="text-xs font-normal text-muted-foreground" id={hintId}>
          {hint}
        </span>
      ) : null}
      {error ? (
        <span className="text-xs font-normal text-destructive" id={errorId} role="alert">
          {error}
        </span>
      ) : null}
    </div>
  );
}
