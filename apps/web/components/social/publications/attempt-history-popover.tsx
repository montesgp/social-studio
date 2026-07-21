"use client";

import { useState } from "react";
import { History } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { PublicationAttemptDto } from "@/lib/api/publications";

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

/** Popover listing every recorded `PublicationAttempt` (design OQ-5) — full auditability, no cap. */
export function AttemptHistoryPopover({ attempts }: { attempts: PublicationAttemptDto[] }) {
  const [open, setOpen] = useState(false);

  if (attempts.length === 0) {
    return <span className="text-xs text-muted-foreground">No attempts yet.</span>;
  }

  return (
    <div className="relative inline-block">
      <Button size="sm" variant="ghost" onClick={() => setOpen((v) => !v)}>
        <History className="h-3.5 w-3.5" /> {attempts.length} attempt{attempts.length === 1 ? "" : "s"}
      </Button>
      {open ? (
        <div className="absolute right-0 top-8 z-20 w-80 rounded-md border border-border bg-background p-3 text-xs shadow-lg">
          <div className="flex flex-col gap-2">
            {[...attempts].reverse().map((attempt) => (
              <div key={attempt.attemptNumber} className="rounded-md border border-border p-2">
                <div className="flex items-center justify-between">
                  <span className="font-semibold">Attempt #{attempt.attemptNumber}</span>
                  <span
                    className={cn(
                      "font-medium",
                      attempt.outcome === "Succeeded" && "text-emerald-600 dark:text-emerald-400",
                      attempt.outcome === "Failed" && "text-rose-600 dark:text-rose-400",
                    )}
                  >
                    {attempt.outcome}
                  </span>
                </div>
                <p className="mt-1 text-muted-foreground">
                  Started {formatDateTime(attempt.startedAtUtc)}
                  {attempt.finishedAtUtc ? ` · Finished ${formatDateTime(attempt.finishedAtUtc)}` : " · In progress"}
                </p>
                {attempt.errorMessage ? <p className="mt-1 text-rose-600 dark:text-rose-400">{attempt.errorMessage}</p> : null}
                {attempt.externalReference ? (
                  <p className="mt-1 text-muted-foreground">Ref: {attempt.externalReference}</p>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}
