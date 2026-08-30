"use client";

import { useEffect, useState } from "react";
import { Layers, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useToast } from "@/components/ui/toaster";
import { CompositionCart, type CompositionCartValue } from "@/components/social/generator/composition-cart";
import {
  listDeliverableFormats,
  startContentBatch,
  type ContentFormatOption
} from "@/lib/api/content-batches";
import type { ApiError } from "@/lib/api/types";

/**
 * "Generate a batch" for one analysis: compose a cart, fire it, walk away.
 *
 * Self-contained on purpose. It owns its own catalog fetch and submit so it can be dropped into any
 * screen that has an analysis and a client, without threading state through a 3000-line parent.
 *
 * Once started it does NOT track the batch itself — the global indicator in the app shell does that,
 * because the whole promise is that the operator can leave this page.
 */

/** Fallback ceiling used until the backend rejects an over-cap cart with the client's real limit. */
const DEFAULT_MAX_PIECES = 20;

interface BatchGenerationPanelProps {
  analysisId: string | null | undefined;
  clientId: string | null | undefined;
  /** Disables composing while a single-format generation is already running on this screen. */
  disabled?: boolean;
}

export function BatchGenerationPanel({ analysisId, clientId, disabled = false }: BatchGenerationPanelProps) {
  const token = useAccessToken();
  const toast = useToast();

  const [formats, setFormats] = useState<ContentFormatOption[]>([]);
  const [cart, setCart] = useState<CompositionCartValue>({ composition: [], assortedTotal: 0 });
  const [submitting, setSubmitting] = useState(false);
  const [lastError, setLastError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;

    let cancelled = false;

    listDeliverableFormats(token)
      .then((entries) => {
        if (!cancelled) setFormats(entries);
      })
      .catch(() => {
        // An empty tile list is self-explanatory in the UI; a toast on mount would be noise.
      });

    return () => {
      cancelled = true;
    };
  }, [token]);

  const totalPieces =
    cart.composition.reduce((sum, item) => sum + item.count, 0) + cart.assortedTotal;

  const canSubmit =
    Boolean(analysisId) && Boolean(clientId) && totalPieces > 0 && !submitting && !disabled;

  async function handleStart() {
    if (!analysisId || !clientId || !canSubmit) return;

    setSubmitting(true);
    setLastError(null);

    try {
      const response = await startContentBatch(
        analysisId,
        {
          clientId,
          composition: cart.composition,
          assortedTotal: cart.assortedTotal > 0 ? cart.assortedTotal : undefined
        },
        token
      );

      // Show the RESOLVED mix, not what was asked: the assorted tile decided part of it, and the
      // operator is entitled to know what they just paid for.
      const mix = response.composition
        .map((entry) => `${entry.count}x ${entry.formatKey}`)
        .join(", ");

      toast.success(`Batch queued — ${response.totalPieces} pieces`, mix);
      setCart({ composition: [], assortedTotal: 0 });
    } catch (error) {
      const message = (error as ApiError).message ?? "Could not start the batch.";
      setLastError(message);
      toast.error("Could not start the batch", message);
    } finally {
      setSubmitting(false);
    }
  }

  if (!analysisId) {
    return null;
  }

  return (
    <section className="rounded-lg border border-border bg-card/40 p-4" aria-label="Batch generation">
      <header className="mb-3 flex items-center justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-sm font-semibold">
            <Layers className="h-4 w-4 text-primary" aria-hidden="true" />
            Generate a batch
          </h2>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Compose how many pieces of each format you want. It runs in the background — you can keep
            working anywhere in Hive and pick the results up in the content bank.
          </p>
        </div>

        <Button type="button" onClick={handleStart} disabled={!canSubmit}>
          {submitting ? (
            <span className="flex items-center gap-2">
              <Loader2 className="h-3 w-3 animate-spin" />
              Queuing…
            </span>
          ) : (
            `Generate ${totalPieces || ""} ${totalPieces === 1 ? "piece" : "pieces"}`.trim()
          )}
        </Button>
      </header>

      {!clientId ? (
        <p className="mb-3 text-xs text-amber-400">
          Pick a client first — pieces are generated against that client&apos;s brand.
        </p>
      ) : null}

      <CompositionCart
        formats={formats}
        value={cart}
        onChange={setCart}
        maxPieces={DEFAULT_MAX_PIECES}
        disabled={disabled || submitting || !clientId}
      />

      {lastError ? <p className="mt-3 text-xs text-destructive">{lastError}</p> : null}
    </section>
  );
}
