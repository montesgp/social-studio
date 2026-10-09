"use client";

import { useCallback, useReducer } from "react";
import type { ContentFormatKey } from "@/lib/api/content-generation";
import {
  buildEnqueueCandidates,
  compositionReducer,
  getCandidateComposition,
  getFormatCount,
  initialCompositionState,
  isCandidateComposed,
  validateComposition
} from "@/lib/social/workflow-composition";

/**
 * Thin `useReducer` wrapper over the pure `compositionReducer` (kept dependency-free for unit testing —
 * see `lib/social/workflow-composition.ts` and its `.test.ts`). Owns the per-candidate format×count state
 * the composition editor and enqueue summary both read.
 */
export function useCandidateComposition() {
  const [state, dispatch] = useReducer(compositionReducer, initialCompositionState);

  const setCount = useCallback(
    (candidateId: string, formatKey: ContentFormatKey, count: number) => {
      dispatch({ type: "set-count", candidateId, formatKey, count });
    },
    []
  );

  const clearCandidate = useCallback((candidateId: string) => {
    dispatch({ type: "clear-candidate", candidateId });
  }, []);

  const reset = useCallback(() => {
    dispatch({ type: "reset" });
  }, []);

  return {
    state,
    setCount,
    clearCandidate,
    reset,
    getComposition: (candidateId: string) => getCandidateComposition(state, candidateId),
    getCount: (candidateId: string, formatKey: ContentFormatKey) => getFormatCount(state, candidateId, formatKey),
    isComposed: (candidateId: string) => isCandidateComposed(state, candidateId),
    validate: (candidateIds: string[]) => validateComposition(state, candidateIds),
    buildPayload: (candidateIds: string[]) => buildEnqueueCandidates(state, candidateIds)
  };
}
