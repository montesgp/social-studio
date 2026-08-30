"use client";

import * as React from "react";
import { cn } from "@/lib/utils";

interface CollapsibleContextValue {
  open: boolean;
  toggle: () => void;
  contentId: string;
}

const CollapsibleContext = React.createContext<CollapsibleContextValue | null>(null);

function useCollapsibleContext(component: string) {
  const ctx = React.useContext(CollapsibleContext);
  if (!ctx) {
    throw new Error(`${component} must be used within a <Collapsible>`);
  }
  return ctx;
}

export interface CollapsibleProps {
  children: React.ReactNode;
  /** Controlled open state. When provided, the component is fully controlled by the caller. */
  open?: boolean;
  /** Uncontrolled initial state. Ignored when `open` is provided. Defaults to `false` (collapsed). */
  defaultOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
  className?: string;
  /** Stable id suffix for the content region; falls back to a generated id when omitted. */
  id?: string;
}

/**
 * Minimal controlled/uncontrolled collapsible container — no external dependency (this repo has no
 * `@radix-ui/react-collapsible`). Composes with `CollapsibleTrigger`/`CollapsibleContent` via context so
 * callers keep the same shape a Radix-style primitive would offer.
 */
export function Collapsible({ children, open, defaultOpen = false, onOpenChange, className, id }: CollapsibleProps) {
  const isControlled = open !== undefined;
  const [uncontrolledOpen, setUncontrolledOpen] = React.useState(defaultOpen);
  const resolvedOpen = isControlled ? open : uncontrolledOpen;
  const generatedId = React.useId();
  const contentId = id ?? generatedId;

  const toggle = React.useCallback(() => {
    const next = !resolvedOpen;
    if (!isControlled) {
      setUncontrolledOpen(next);
    }
    onOpenChange?.(next);
  }, [isControlled, onOpenChange, resolvedOpen]);

  const value = React.useMemo<CollapsibleContextValue>(
    () => ({ open: resolvedOpen, toggle, contentId }),
    [resolvedOpen, toggle, contentId]
  );

  return (
    <CollapsibleContext.Provider value={value}>
      <div className={className}>{children}</div>
    </CollapsibleContext.Provider>
  );
}

export interface CollapsibleTriggerProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean;
}

export const CollapsibleTrigger = React.forwardRef<HTMLButtonElement, CollapsibleTriggerProps>(
  ({ className, children, onClick, ...props }, ref) => {
    const { open, toggle, contentId } = useCollapsibleContext("CollapsibleTrigger");

    return (
      <button
        {...props}
        ref={ref}
        type="button"
        aria-expanded={open}
        aria-controls={contentId}
        onClick={(event) => {
          onClick?.(event);
          if (!event.defaultPrevented) {
            toggle();
          }
        }}
        className={cn("inline-flex items-center gap-1.5", className)}
      >
        {children}
      </button>
    );
  }
);
CollapsibleTrigger.displayName = "CollapsibleTrigger";

export type CollapsibleContentProps = React.HTMLAttributes<HTMLDivElement>;

export const CollapsibleContent = React.forwardRef<HTMLDivElement, CollapsibleContentProps>(
  ({ className, children, ...props }, ref) => {
    const { open, contentId } = useCollapsibleContext("CollapsibleContent");

    if (!open) {
      return <div ref={ref} id={contentId} hidden aria-hidden="true" />;
    }

    return (
      <div {...props} ref={ref} id={contentId} className={className}>
        {children}
      </div>
    );
  }
);
CollapsibleContent.displayName = "CollapsibleContent";
