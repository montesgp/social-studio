import type { ContentFormatKey } from "@/lib/api/content-generation";

/** One format + how many pieces of it to generate for a single candidate. */
export interface CompositionEntry {
  formatKey: ContentFormatKey;
  count: number;
}

/** `candidateId -> chosen formats`. Absent key or empty array both mean "nothing chosen yet". */
export type CompositionState = Record<string, CompositionEntry[]>;

export type CompositionAction =
  | { type: "set-count"; candidateId: string; formatKey: ContentFormatKey; count: number }
  | { type: "clear-candidate"; candidateId: string }
  | { type: "reset" };

export const initialCompositionState: CompositionState = {};

function removeCandidate(state: CompositionState, candidateId: string): CompositionState {
  const next: CompositionState = {};
  for (const key of Object.keys(state)) {
    if (key !== candidateId) {
      next[key] = state[key]!;
    }
  }
  return next;
}

/**
 * Pure reducer over the per-candidate format×count composition. Kept dependency-free (no React import)
 * so it is unit-testable with the repo's plain `tsx --test` runner; `hooks/use-candidate-composition.ts`
 * wraps this with `useReducer`.
 *
 * `set-count` with `count <= 0` removes the entry entirely (spec: a candidate with zero formats chosen
 * must block enqueue, so "chosen with count 0" and "never chosen" must be the same state).
 */
export function compositionReducer(state: CompositionState, action: CompositionAction): CompositionState {
  switch (action.type) {
    case "set-count": {
      const { candidateId, formatKey, count } = action;
      const current = state[candidateId] ?? [];
      const withoutFormat = current.filter((entry) => entry.formatKey !== formatKey);
      const next = count > 0 ? [...withoutFormat, { formatKey, count }] : withoutFormat;

      if (next.length === 0) {
        return removeCandidate(state, candidateId);
      }

      return { ...state, [candidateId]: next };
    }
    case "clear-candidate": {
      return removeCandidate(state, action.candidateId);
    }
    case "reset":
      return {};
    default:
      return state;
  }
}

export function getCandidateComposition(state: CompositionState, candidateId: string): CompositionEntry[] {
  return state[candidateId] ?? [];
}

export function getFormatCount(state: CompositionState, candidateId: string, formatKey: ContentFormatKey): number {
  return getCandidateComposition(state, candidateId).find((entry) => entry.formatKey === formatKey)?.count ?? 0;
}

export function isCandidateComposed(state: CompositionState, candidateId: string): boolean {
  return getCandidateComposition(state, candidateId).length > 0;
}

export interface CompositionValidationResult {
  valid: boolean;
  /** Candidate ids from the input list that currently have zero formats chosen. */
  emptyCandidateIds: string[];
}

/** Spec: "Empty composition blocks enqueue" — every selected candidate must have at least one format. */
export function validateComposition(state: CompositionState, candidateIds: string[]): CompositionValidationResult {
  const emptyCandidateIds = candidateIds.filter((id) => !isCandidateComposed(state, id));
  return { valid: emptyCandidateIds.length === 0, emptyCandidateIds };
}

export interface EnqueueCandidatePayload {
  candidateId: string;
  composition: CompositionEntry[];
}

/** Builds the enqueue request payload for candidates that have a non-empty composition. */
export function buildEnqueueCandidates(state: CompositionState, candidateIds: string[]): EnqueueCandidatePayload[] {
  return candidateIds
    .filter((id) => isCandidateComposed(state, id))
    .map((candidateId) => ({ candidateId, composition: getCandidateComposition(state, candidateId) }));
}
