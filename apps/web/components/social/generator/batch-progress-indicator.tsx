"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Loader2, PackageCheck, TriangleAlert } from "lucide-react";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { listActiveBatches, type BatchProgress } from "@/lib/api/content-batches";

/**
 * Global batch progress, mounted in the app shell.
 *
 * This is what makes background generation real for the operator: it lives outside the Generator
 * page, so walking over to Clients or Publications does not lose sight of the work. It polls rather
 * than holding a socket — a batch is minutes long, and a poll survives a sleeping laptop and a
 * dropped connection without any reconnect machinery.
 */

const POLL_INTERVAL_MS = 5000;

export function BatchProgressIndicator() {
  const token = useAccessToken();
  const { orgId, isInitialized } = useActiveOrg();
  const [batches, setBatches] = useState<BatchProgress[]>([]);
  const [justFinished, setJustFinished] = useState<BatchProgress | null>(null);
  const previousRef = useRef<BatchProgress[]>([]);

  useEffect(() => {
    if (!isInitialized || !orgId || !token) return;

    let cancelled = false;

    async function poll() {
      try {
        const active = await listActiveBatches(token);
        if (cancelled) return;

        // A batch that was active last tick and is gone now has finished. Surfacing it briefly is the
        // whole payoff: the operator walked away and needs to be told the bank has pieces waiting.
        const finished = previousRef.current.find(
          (previous) => !active.some((current) => current.batchId === previous.batchId)
        );

        if (finished) {
          setJustFinished(finished);
        }

        previousRef.current = active;
        setBatches(active);
      } catch {
        // A failed poll is not worth a toast every five seconds; the next tick recovers.
      }
    }

    void poll();
    const timer = window.setInterval(poll, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [isInitialized, orgId, token]);

  if (batches.length === 0 && !justFinished) {
    return null;
  }

  return (
    <div className="space-y-2">
      {batches.map((batch) => (
        <ActiveBatchRow key={batch.batchId} batch={batch} />
      ))}

      {batches.length === 0 && justFinished ? (
        <FinishedBatchRow batch={justFinished} onDismiss={() => setJustFinished(null)} />
      ) : null}
    </div>
  );
}

function ActiveBatchRow({ batch }: { batch: BatchProgress }) {
  const done = batch.completedPieces + batch.failedPieces;
  const percentage = batch.totalPieces > 0 ? Math.round((done / batch.totalPieces) * 100) : 0;

  return (
    <div className="rounded-lg border border-border bg-card/60 px-3 py-2">
      <div className="flex items-center justify-between gap-3 text-xs">
        <span className="flex items-center gap-2 font-medium">
          <Loader2 className="h-3 w-3 animate-spin text-primary" aria-hidden="true" />
          Generating {done} of {batch.totalPieces}
        </span>
        <span className="text-muted-foreground">
          {batch.currentFormatKey ? `now: ${batch.currentFormatKey}` : "queued"}
        </span>
      </div>

      <div
        className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-valuenow={percentage}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="Batch generation progress"
      >
        <div className="h-full bg-primary transition-all" style={{ width: `${percentage}%` }} />
      </div>

      {batch.failedPieces > 0 ? (
        <p className="mt-1.5 flex items-center gap-1 text-[11px] text-amber-400">
          <TriangleAlert className="h-3 w-3" aria-hidden="true" />
          {batch.failedPieces} piece(s) failed — the rest keep going.
        </p>
      ) : null}
    </div>
  );
}

function FinishedBatchRow({
  batch,
  onDismiss
}: {
  batch: BatchProgress;
  onDismiss: () => void;
}) {
  return (
    <div className="rounded-lg border border-primary/40 bg-primary/5 px-3 py-2">
      <div className="flex items-center justify-between gap-3 text-xs">
        <span className="flex items-center gap-2 font-medium">
          <PackageCheck className="h-3 w-3 text-primary" aria-hidden="true" />
          Batch ready — {batch.completedPieces} piece(s) in the bank
        </span>

        <div className="flex items-center gap-2">
          <Link href="/publications" className="text-primary hover:underline" onClick={onDismiss}>
            Open
          </Link>
          <button type="button" onClick={onDismiss} className="text-muted-foreground hover:text-foreground">
            Dismiss
          </button>
        </div>
      </div>
    </div>
  );
}
