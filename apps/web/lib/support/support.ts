// Centralized Incoders support actions.
// Reusable across the whole app wherever a user-facing error may reach the end user.
// Two channels: (a) report to support (async issue), (b) urgent direct contact (WhatsApp).

export const INCODERS_SUPPORT = {
  whatsappPhone: "5490000000000",
  whatsappBaseUrl: "https://api.whatsapp.com/send/",
  email: "support@incoders.com.ar",
} as const;

export interface SupportErrorContext {
  /** App area where the error happened, e.g. "single-analyzer". */
  feature: string;
  /** Stable backend error code, when available. */
  errorCode?: string;
  /** Raw error message (diagnostic — not necessarily shown to the user). */
  errorMessage?: string;
  /** Organization the user was operating under, when relevant. */
  organizationId?: string;
  /** Extra diagnostic key/values. */
  details?: Record<string, string | number | boolean | null | undefined>;
}

function buildDiagnosticSummary(context: SupportErrorContext): string {
  const lines: (string | null)[] = [
    `Feature: ${context.feature}`,
    context.errorCode ? `Error code: ${context.errorCode}` : null,
    context.errorMessage ? `Message: ${context.errorMessage}` : null,
    context.organizationId ? `Organization: ${context.organizationId}` : null,
    `When: ${new Date().toISOString()}`,
  ];

  if (context.details) {
    for (const [key, value] of Object.entries(context.details)) {
      if (value !== undefined && value !== null && value !== "") {
        lines.push(`${key}: ${value}`);
      }
    }
  }

  return lines.filter((line): line is string => Boolean(line)).join("\n");
}

/** Opens Incoders WhatsApp for urgent, direct support contact, prefilled with diagnostics. */
export function openWhatsAppSupport(context?: SupportErrorContext): void {
  if (typeof window === "undefined") return;

  const text = context
    ? `Hi Incoders support, I need urgent help with an error.\n\n${buildDiagnosticSummary(context)}`
    : "Hi Incoders support, I need help.";

  const url =
    `${INCODERS_SUPPORT.whatsappBaseUrl}?phone=${INCODERS_SUPPORT.whatsappPhone}` +
    `&text=${encodeURIComponent(text)}&type=phone_number&app_absent=0`;

  window.open(url, "_blank", "noopener,noreferrer");
}

/**
 * Files a support report for an error the user hit.
 *
 * INTERIM: opens a prefilled support email so the report reaches the team today.
 * The automated issue system (a GitHub/Jira issue per project + client) is not wired yet.
 * TODO: replace the mailto with a POST to the Incoders issues endpoint once it exists,
 * keeping this same signature so callers don't change.
 */
export function reportSupportIssue(context: SupportErrorContext): void {
  if (typeof window === "undefined") return;

  const subject = `[${context.feature}] ${context.errorCode ?? "Error report"}`;
  const body =
    "Please describe what you were doing when this happened:\n\n\n" +
    `---\nDiagnostic details (please keep):\n${buildDiagnosticSummary(context)}`;

  const url =
    `mailto:${INCODERS_SUPPORT.email}` +
    `?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;

  window.open(url, "_blank", "noopener,noreferrer");
}
