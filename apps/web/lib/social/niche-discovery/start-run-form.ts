/**
 * Start-run form validation (ND-01, design §10). The analysis window and the depth are
 * operator-chosen, explicit owner decisions — not defaults of convenience — with a 90-day window
 * default matching the backend `DiscoveryWindow` value object (MinDays=7, MaxDays=365,
 * DefaultDays=90) and the `youtube`-only network / `video`|`short` content-format constraints the
 * backend enforces today.
 */

import type { StartDiscoveryRunRequest } from "@/lib/api/niche-discovery";

export const MIN_WINDOW_DAYS = 7;
export const MAX_WINDOW_DAYS = 365;
export const DEFAULT_WINDOW_DAYS = 90;

export const DEPTH_OPTIONS = ["Quick", "Standard", "Deep"] as const;
export type DiscoveryDepthOption = (typeof DEPTH_OPTIONS)[number];

export const CONTENT_FORMAT_OPTIONS = ["video", "short"] as const;
export type ContentFormatOption = (typeof CONTENT_FORMAT_OPTIONS)[number];

export const NETWORK_KEY_OPTIONS = ["youtube"] as const;

export interface StartRunFormState {
  sourceMarketCode: string;
  targetMarketCode: string;
  networkKey: string;
  contentFormat: ContentFormatOption;
  windowDays: number;
  depth: DiscoveryDepthOption;
  /** Free text, comma- or newline-separated. See {@link parseSeedTerms}. */
  seedTermsText: string;
}

export type StartRunFormError = "sourceMarketCode" | "targetMarketCode" | "windowDays";

/**
 * Seed terms are what the backend actually searches: each one becomes a YouTube `q`. They are NEVER
 * translated anywhere in the pipeline, so an operator targeting a non-English language must type in
 * that language — the term reaches the provider verbatim.
 */
export function parseSeedTerms(text: string): string[] {
  const seen = new Set<string>();

  for (const term of text.split(/[,\n]/)) {
    const trimmed = term.trim();
    if (trimmed) {
      seen.add(trimmed);
    }
  }

  return [...seen];
}

export interface StartRunFormValidation {
  valid: boolean;
  errors: StartRunFormError[];
}

export function createDefaultStartRunFormState(): StartRunFormState {
  return {
    sourceMarketCode: "",
    targetMarketCode: "",
    networkKey: NETWORK_KEY_OPTIONS[0],
    contentFormat: "video",
    windowDays: DEFAULT_WINDOW_DAYS,
    depth: "Standard",
    seedTermsText: "",
  };
}

export function validateStartRunForm(state: StartRunFormState): StartRunFormValidation {
  const errors: StartRunFormError[] = [];

  if (!state.sourceMarketCode) {
    errors.push("sourceMarketCode");
  }
  if (!state.targetMarketCode) {
    errors.push("targetMarketCode");
  }
  // Source == target is DELIBERATELY allowed: the backend never enforced otherwise, and asking
  // "where is the gap inside this language" is a legitimate study.
  //
  // Seed terms are DELIBERATELY optional: with none, the backend runs a query-less trending sweep and
  // the niches emerge from the corpus. Demanding a term would make the operator guess the answer
  // before the tool looks for it.
  if (state.windowDays < MIN_WINDOW_DAYS || state.windowDays > MAX_WINDOW_DAYS) {
    errors.push("windowDays");
  }

  return { valid: errors.length === 0, errors };
}

export function buildStartRunRequest(state: StartRunFormState): StartDiscoveryRunRequest {
  return {
    sourceMarketCode: state.sourceMarketCode,
    targetMarketCode: state.targetMarketCode,
    networkKey: state.networkKey,
    contentFormat: state.contentFormat,
    windowDays: state.windowDays,
    depth: state.depth,
    seedTerms: parseSeedTerms(state.seedTermsText),
  };
}
