"use client";

import { AlertTriangle, LifeBuoy, MessageCircle, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  openWhatsAppSupport,
  reportSupportIssue,
  type SupportErrorContext,
} from "@/lib/support/support";
import { resolveUserFacingError } from "@/lib/support/user-facing-errors";

export interface SupportErrorValue {
  code?: string;
  message?: string;
}

export interface SupportErrorProps {
  /** The error to surface, or null to render nothing. */
  error: SupportErrorValue | null;
  /** Where this happened — used to enrich the support report. */
  context: Omit<SupportErrorContext, "errorCode" | "errorMessage">;
  onDismiss?: () => void;
  className?: string;
}

/**
 * Reusable user-facing error panel with built-in support actions.
 * Drop it anywhere an error can reach the end user. It translates backend error
 * codes into friendly copy and offers two support channels:
 *   - Report to support (async issue; today opens a prefilled email).
 *   - Contact support on WhatsApp (urgent, direct).
 */
export function SupportError({ error, context, onDismiss, className }: SupportErrorProps) {
  if (!error) {
    return null;
  }

  const resolved = resolveUserFacingError(error);
  const supportContext: SupportErrorContext = {
    ...context,
    errorCode: error.code,
    errorMessage: error.message,
  };

  return (
    <div
      role="alert"
      className={cn(
        "rounded-lg border border-red-500/30 bg-red-500/10 p-4 text-red-700 dark:text-red-200",
        className
      )}
    >
      <div className="flex items-start gap-3">
        <AlertTriangle className="h-5 w-5 shrink-0 mt-0.5" aria-hidden="true" />
        <div className="flex-1 min-w-0 space-y-1">
          <p className="font-medium">{resolved.title}</p>
          <p className="text-sm opacity-90">{resolved.message}</p>
        </div>
        {onDismiss && (
          <button
            type="button"
            className="opacity-70 hover:opacity-100"
            onClick={onDismiss}
            aria-label="Dismiss"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 text-xs"
          onClick={() => reportSupportIssue(supportContext)}
        >
          <LifeBuoy className="h-3.5 w-3.5" /> Report to support
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 text-xs"
          onClick={() => openWhatsAppSupport(supportContext)}
        >
          <MessageCircle className="h-3.5 w-3.5" /> Contact support (urgent)
        </Button>
      </div>
    </div>
  );
}
