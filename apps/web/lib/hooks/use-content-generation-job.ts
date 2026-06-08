"use client";

import { useCallback, useEffect, useReducer, useRef } from "react";
import { getJobStatus } from "@/lib/api/content-generation";
import type {
  ContentGenerationJobStatus,
  ContentGenerationJobStatusResponse,
  GeneratedPackageManifest,
} from "@/lib/api/types";

const DEFAULT_INTERVAL_MS = 2500;
// Client-side safety net so the overlay can never poll forever. The backend force-fails a hung
// job (per-job timeout ~15 min, watchdog ~16 min), so by this point a terminal status should
// already have arrived; if it hasn't, the status endpoint is unreachable and we stop gracefully.
const DEFAULT_TIMEOUT_MS = 16 * 60 * 1000;

/** Terminal statuses where polling should stop. */
const TERMINAL_STATUSES = new Set<ContentGenerationJobStatus>([
  "Completed",
  "Partial",
  "PartialSuccess",
  "Failed",
]);

// ── State machine ─────────────────────────────────────────────────────────────

type HookState =
  | { phase: "idle" }
  | { phase: "loading" }
  | { phase: "polling"; data: ContentGenerationJobStatusResponse }
  | { phase: "done"; data: ContentGenerationJobStatusResponse }
  | { phase: "error"; error: Error };

type HookAction =
  | { type: "FETCH_START" }
  | { type: "FETCH_SUCCESS"; payload: ContentGenerationJobStatusResponse }
  | { type: "FETCH_ERROR"; payload: Error }
  | { type: "RESET" };

function reducer(state: HookState, action: HookAction): HookState {
  switch (action.type) {
    case "FETCH_START":
      return { phase: "loading" };
    case "FETCH_SUCCESS": {
      const isTerminal = TERMINAL_STATUSES.has(action.payload.status);
      return isTerminal
        ? { phase: "done", data: action.payload }
        : { phase: "polling", data: action.payload };
    }
    case "FETCH_ERROR":
      return { phase: "error", error: action.payload };
    case "RESET":
      return { phase: "idle" };
    default:
      return state;
  }
}

// ── Hook result type ──────────────────────────────────────────────────────────

export interface UseContentGenerationJobResult {
  status: ContentGenerationJobStatus | null;
  perFormatStatus: ContentGenerationJobStatusResponse["perFormatStatus"];
  manifest: GeneratedPackageManifest | null;
  /** Backend failure reason (codes embedded) when the job did not succeed. */
  statusReason: string | null;
  isLoading: boolean;
  isPolling: boolean;
  isError: boolean;
  error: Error | null;
}

// ── Hook implementation ───────────────────────────────────────────────────────

/**
 * Polls a content generation job until it reaches a terminal state.
 *
 * - Starts polling immediately when `jobId` is non-null and `enabled !== false`.
 * - Polls every `intervalMs` (default 2500ms).
 * - Stops automatically on Completed | Partial | Failed.
 * - Cancels in-flight requests on unmount or jobId change (AbortController).
 * - Safe under React Strict Mode double-mount (abort + re-fetch on second mount).
 */
export function useContentGenerationJob(
  jobId: string | null,
  opts?: { intervalMs?: number; enabled?: boolean; token?: string; timeoutMs?: number }
): UseContentGenerationJobResult {
  const [state, dispatch] = useReducer(reducer, { phase: "idle" });
  const intervalMs = opts?.intervalMs ?? DEFAULT_INTERVAL_MS;
  const timeoutMs = opts?.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const enabled = opts?.enabled !== false;
  const token = opts?.token;

  // Stable ref to hold the current AbortController so cleanup can cancel it.
  const abortRef = useRef<AbortController | null>(null);
  // Track whether the current effect cycle is still alive (Strict Mode safety).
  const cancelledRef = useRef(false);

  const fetchStatus = useCallback(
    async (id: string, signal: AbortSignal) => {
      try {
        const response = await getJobStatus(id, { signal, token });
        return response;
      } catch (err) {
        if ((err as { name?: string }).name === "AbortError") return null;
        throw err;
      }
    },
    [token]
  );

  useEffect(() => {
    if (!jobId || !enabled) {
      dispatch({ type: "RESET" });
      return;
    }

    cancelledRef.current = false;
    const controller = new AbortController();
    abortRef.current = controller;

    dispatch({ type: "FETCH_START" });

    let timeoutId: ReturnType<typeof setTimeout>;
    const startedAt = Date.now();

    async function poll() {
      if (cancelledRef.current || controller.signal.aborted) return;

      // Client-side safety net: stop polling and surface a timeout instead of spinning forever.
      if (Date.now() - startedAt > timeoutMs) {
        dispatch({
          type: "FETCH_ERROR",
          payload: new Error(
            "Generation is taking longer than expected. It may still finish in the background — " +
              "check your history in a few moments."
          ),
        });
        return;
      }

      try {
        const response = await fetchStatus(jobId as string, controller.signal);
        if (!response || cancelledRef.current) return;

        dispatch({ type: "FETCH_SUCCESS", payload: response });

        // If terminal, stop polling
        if (TERMINAL_STATUSES.has(response.status)) return;

        // Schedule next poll
        if (!cancelledRef.current && !controller.signal.aborted) {
          timeoutId = setTimeout(() => {
            void poll();
          }, intervalMs);
        }
      } catch (err) {
        if (cancelledRef.current) return;
        dispatch({ type: "FETCH_ERROR", payload: err instanceof Error ? err : new Error(String(err)) });
      }
    }

    void poll();

    return () => {
      cancelledRef.current = true;
      clearTimeout(timeoutId);
      controller.abort();
      abortRef.current = null;
      dispatch({ type: "RESET" });
    };
  }, [jobId, enabled, intervalMs, timeoutMs, fetchStatus]);

  const data =
    state.phase === "polling" || state.phase === "done" ? state.data : null;

  return {
    status: data?.status ?? null,
    perFormatStatus: data?.perFormatStatus ?? {},
    manifest: data?.manifest ?? null,
    statusReason: data?.statusReason ?? null,
    isLoading: state.phase === "loading",
    isPolling: state.phase === "polling",
    isError: state.phase === "error",
    error: state.phase === "error" ? state.error : null,
  };
}
