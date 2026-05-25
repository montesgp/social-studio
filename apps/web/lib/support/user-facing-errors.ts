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
  "integrations.default_ai_provider_not_connected": {
    title: "No AI provider is connected",
    message:
      "No AI provider is connected for your organization. An admin needs to connect one (e.g. Claude) in Settings → Integrations.",
    isConfigIssue: true,
  },
};

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
