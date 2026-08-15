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
}

export type StartRunFormError =
  | "sourceMarketCode"
  | "targetMarketCode"
  | "marketsMustDiffer"
  | "windowDays";

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
  if (
    state.sourceMarketCode &&
    state.targetMarketCode &&
    state.sourceMarketCode === state.targetMarketCode
  ) {
    errors.push("marketsMustDiffer");
  }
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
  };
}
