// Maps backend error codes to friendly, non-technical, actionable copy for end users.
// English for now; a future Spanish translation layer can wrap resolveUserFacingError.

export interface UserFacingError {
  title: string;
  message: string;
  /** True when an org admin can fix it via Settings → Integrations (worth offering support). */
  isConfigIssue: boolean;
}

export interface UserFacingErrorInput {
  code?: string;
  message?: string;
}

const ERROR_COPY: Record<string, UserFacingError> = {
  "integrations.provider_credential_unavailable": {
    title: "Data extraction isn't available",
    message:
      "We couldn't reach the data extraction service for your organization. It may not be configured yet, or its credentials are missing or invalid.",
    isConfigIssue: true,
  },
  "content_extraction.provider_not_configured": {
    title: "Extraction service not configured",
    message:
      "The data extraction service is not set up for your organization. An admin needs to configure it in Settings → Integrations.",
    isConfigIssue: true,
  },
  "content_extraction.provider_credentials_invalid": {
    title: "Extraction credentials were rejected",
    message:
      "The data extraction service rejected the saved credentials. They may be invalid or expired — please update them in Settings → Integrations.",
    isConfigIssue: true,
  },
  "content_extraction.actor_not_configured": {
    title: "Extraction isn't configured for this platform",
    message:
      "No extraction actor is configured for this platform. An admin can add it in Settings → Integrations.",
    isConfigIssue: true,
  },
  "content_extraction.empty_response": {
    title: "No data found",
    message: "The source returned no data. The link may be private, removed, or unsupported.",
    isConfigIssue: false,
  },
  "integrations.provider_timeout": {
    title: "The extraction service took too long",
    message: "The source took too long to respond. Please try again in a moment.",
    isConfigIssue: false,
  },
  "integrations.external_provider_unavailable": {
    title: "Extraction service unavailable",
    message: "The extraction service is temporarily unavailable. Please try again shortly.",
    isConfigIssue: false,
  },
  "integrations.provider_rate_limited": {
    title: "Too many requests",
    message: "The extraction service is rate-limited right now. Please wait a bit and try again.",
    isConfigIssue: false,
  },
  "social.single_analyzer.extraction_provider_unavailable": {
    title: "Data extraction service unavailable",
    message:
      "We didn't start the analysis because the data extraction service isn't reachable right now. Check it in Settings → Integrations, then try again.",
    isConfigIssue: true,
  },
  "social.single_analyzer.ai_provider_unavailable": {
    title: "AI service unavailable",
    message:
      "We didn't start the analysis because the AI service isn't reachable right now. Check it in Settings → Integrations, then try again.",
    isConfigIssue: true,
  },
  "integrations.ai_json_invalid": {
    title: "The AI response couldn't be used",
    message: "The AI returned a response we couldn't read. Please try again in a moment.",
    isConfigIssue: false,
  },
  "social.single_analyzer.not_found": {
    title: "Analysis not found",
    message:
      "We couldn't find this analysis. It may have been removed, or it doesn't belong to your organization.",
    isConfigIssue: false,
  },
  "social.single_analyzer.analysis_failed": {
    title: "Analysis failed",
    message:
      "Something went wrong while analyzing the content. Please try again — if it keeps happening, let support know.",
    isConfigIssue: false,
  },
  "server.unexpected_error": {
    title: "Something didn't work on our side",
    message:
      "We hit an unexpected problem processing your request. Please try again — if it keeps happening, let support know.",
    isConfigIssue: false,
  },
  "integrations.default_ai_provider_not_connected": {
    title: "No AI provider is connected",
    message:
      "No AI provider is connected for your organization. An admin needs to connect one (e.g. Claude) in Settings → Integrations.",
    isConfigIssue: true,
  },
  "integrations.ai_route_not_configured": {
    title: "AI isn't set up for this workflow yet",
    message:
      "This analysis can't run because no AI provider is configured for your organization's workflow yet. An admin needs to set up the AI routing in Settings → Integrations. If you're not sure how, contact support and we'll help you get it configured.",
    isConfigIssue: true,
  },
  // ── Content generation (flyer/carousel) failures ────────────────────────────
  // These are usually NOT fixed by retrying as-is (a blind retry just re-spends AI
  // credits), so the copy points the user to support instead of nudging "try again".
  "social.content_generation.failed": {
    title: "We couldn't generate this content",
    message:
      "The generation didn't complete. Retrying as-is usually won't fix it and re-spends AI credits — contact support and we'll sort out what's blocking it.",
    isConfigIssue: false,
  },
  "visual-guardrail.off-palette": {
    title: "The design didn't match the client's brand palette",
    message:
      "The AI laid out the content with a color that isn't in this client's brand palette, so we blocked it to protect the brand. This needs a brand-setup adjustment — contact support and we'll fix it for you.",
    isConfigIssue: true,
  },
  "visual-guardrail.off-font": {
    title: "The design used a font outside the brand kit",
    message:
      "The AI used a typeface that isn't in this client's brand kit, so we blocked it. This needs a brand-setup adjustment — contact support and we'll fix it for you.",
    isConfigIssue: true,
  },
  "visual-designer.empty-markup": {
    title: "The AI design came back empty",
    message:
      "The design step returned nothing usable. Contact support if you need this content — we can look into it without you burning more credits.",
    isConfigIssue: false,
  },
};

// Specific failure codes the backend embeds inside a job's aggregate status_reason
// (e.g. "render_failed[flyer: guardrail-failed:visual-guardrail.off-palette: …]").
// Order matters: first match wins.
const GENERATION_FAILURE_CODES: readonly string[] = [
  "visual-guardrail.off-palette",
  "visual-guardrail.off-font",
  "visual-designer.empty-markup",
];

/**
 * Maps a content-generation job's failure reason to a {@link UserFacingErrorInput} for
 * {@link SupportError}: a recognized specific code when present, otherwise a friendly generic
 * code. The raw reason is kept as the message so the support report carries the technical detail.
 */
export function resolveGenerationFailureError(statusReason?: string | null): UserFacingErrorInput {
  const reason = statusReason ?? "";
  const specific = GENERATION_FAILURE_CODES.find((code) => reason.includes(code));
  return {
    code: specific ?? "social.content_generation.failed",
    message: statusReason ?? undefined,
  };
}

const DEFAULT_ERROR: UserFacingError = {
  title: "Something went wrong",
  message: "We hit an unexpected problem while processing your request. Please try again.",
  isConfigIssue: false,
};

export function resolveUserFacingError(input: UserFacingErrorInput): UserFacingError {
  const byCode = input.code ? ERROR_COPY[input.code] : undefined;
  if (byCode) {
    return byCode;
  }

  // Some errors surface the stable code inside the message string.
  if (input.message) {
    const matchedKey = Object.keys(ERROR_COPY).find((key) => input.message!.includes(key));
    if (matchedKey) {
      return ERROR_COPY[matchedKey];
    }
  }

  // A technical code we don't recognize: never show it raw to the user.
  if (input.code) {
    return DEFAULT_ERROR;
  }

  // No code: treat the message as already user-facing copy (e.g. inline hints).
  return { ...DEFAULT_ERROR, message: input.message?.trim() || DEFAULT_ERROR.message };
}
