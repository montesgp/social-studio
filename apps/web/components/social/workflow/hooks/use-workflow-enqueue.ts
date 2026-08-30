"use client";

import { useCallback, useState } from "react";
import { enqueueWorkflowCandidates } from "@/lib/api/content-workflow";
import type { ApiError, EnqueueCandidateCompositionPayload, EnqueueWorkflowCandidatesResponse } from "@/lib/api/types";
import { summarizeEnqueueResponse, type EnqueueFeedbackSummary } from "@/lib/social/workflow-enqueue-summary";

interface UseWorkflowEnqueueResult {
  busy: boolean;
  error: string | null;
  response: EnqueueWorkflowCandidatesResponse | null;
  summary: EnqueueFeedbackSummary | null;
  enqueue: (
    organizationId: string,
    workflowId: string,
    clientId: string,
    candidates: EnqueueCandidateCompositionPayload[],
    token: string | undefined
  ) => Promise<EnqueueWorkflowCandidatesResponse | null>;
  reset: () => void;
}

/**
 * Posts the composed candidates ONCE and renders whatever the backend returns — accepted/rejected per
 * candidate, an ETA, and the content-generation-jobs link. Never polls (spec: "MUST NOT block the UI
 * with a synchronous poll" / design decision #3).
 */
export function useWorkflowEnqueue(): UseWorkflowEnqueueResult {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [response, setResponse] = useState<EnqueueWorkflowCandidatesResponse | null>(null);

  const enqueue = useCallback(
    async (
      organizationId: string,
      workflowId: string,
      clientId: string,
      candidates: EnqueueCandidateCompositionPayload[],
      token: string | undefined
    ) => {
      setBusy(true);
      setError(null);

      try {
        const result = await enqueueWorkflowCandidates(organizationId, workflowId, clientId, candidates, token);
        setResponse(result);
        return result;
      } catch (err) {
        setError((err as ApiError).message);
        return null;
      } finally {
        setBusy(false);
      }
    },
    []
  );

  const reset = useCallback(() => {
    setResponse(null);
    setError(null);
  }, []);

  return {
    busy,
    error,
    response,
    summary: response ? summarizeEnqueueResponse(response) : null,
    enqueue,
    reset
  };
}
