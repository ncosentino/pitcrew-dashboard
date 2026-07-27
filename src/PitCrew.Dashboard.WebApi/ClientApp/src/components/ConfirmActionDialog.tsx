import type { ComponentProps, ReactElement, ReactNode } from 'react';

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import type { Button } from '@/components/ui/button';

interface ConfirmActionDialogProps {
  readonly trigger: ReactElement;
  readonly title: string;
  readonly description: string;
  readonly confirmLabel: string;
  readonly confirmVariant?: ComponentProps<typeof Button>['variant'];
  /** Additional structured evidence rendered between the description and the actions. */
  readonly details?: ReactNode;
  /** Blocks confirmation until the caller's own preconditions hold. */
  readonly confirmDisabled?: boolean;
  /** Opt-in controlled open state for callers that must invalidate a confirmation. */
  readonly open?: boolean;
  readonly onOpenChange?: (open: boolean) => void;
  readonly onConfirm: () => void | Promise<void>;
}

/** Presents an accessible confirmation before invoking one explicit operator action. */
export function ConfirmActionDialog({
  trigger,
  title,
  description,
  confirmLabel,
  confirmVariant = 'default',
  details,
  confirmDisabled = false,
  open,
  onOpenChange,
  onConfirm,
}: ConfirmActionDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogTrigger asChild>{trigger}</AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        {details}
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant={confirmVariant}
            disabled={confirmDisabled}
            onClick={() => void onConfirm()}
          >
            {confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
