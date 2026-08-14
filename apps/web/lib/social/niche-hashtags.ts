/**
 * Client-side mirror of the `Niche` domain rules for curated search hashtags.
 *
 * The server is still the authority — this exists so the operator gets the rule at the moment they
 * type it, instead of a 400 after saving the whole form.
 */

export const NICHE_HASHTAG_MIN_LENGTH = 2;
export const NICHE_HASHTAG_MAX_LENGTH = 64;
export const MAX_NICHE_HASHTAGS = 20;

const HASHTAG_PATTERN = /^[a-z0-9_]+$/;

/** Strips a leading `#`, trims and lowercases — exactly what the domain does before validating. */
export function normalizeNicheHashtag(raw: string): string {
  return raw.trim().replace(/^#+/, "").trim().toLowerCase();
}

/** Returns an error message, or `null` when the (already normalized) hashtag is acceptable. */
export function validateNicheHashtag(normalized: string): string | null {
  if (
    normalized.length < NICHE_HASHTAG_MIN_LENGTH ||
    normalized.length > NICHE_HASHTAG_MAX_LENGTH
  ) {
    return `Each hashtag must be between ${NICHE_HASHTAG_MIN_LENGTH} and ${NICHE_HASHTAG_MAX_LENGTH} characters.`;
  }

  if (!HASHTAG_PATTERN.test(normalized)) {
    return "Each hashtag must be lowercase alphanumeric or underscore, with no spaces.";
  }

  return null;
}

export interface AddHashtagResult {
  hashtags: string[];
  error: string | null;
}

/**
 * Adds one entry to the curated list. An empty entry is a silent no-op (the operator pressed Enter
 * on an empty box); anything else that cannot be added explains itself.
 */
export function addNicheHashtag(current: readonly string[], raw: string): AddHashtagResult {
  const normalized = normalizeNicheHashtag(raw);
  if (normalized.length === 0) {
    return { hashtags: [...current], error: null };
  }

  const invalid = validateNicheHashtag(normalized);
  if (invalid) {
    return { hashtags: [...current], error: invalid };
  }

  if (current.includes(normalized)) {
    return { hashtags: [...current], error: `"${normalized}" is already curated for this niche.` };
  }

  if (current.length >= MAX_NICHE_HASHTAGS) {
    return {
      hashtags: [...current],
      error: `A niche may curate at most ${MAX_NICHE_HASHTAGS} hashtags.`,
    };
  }

  return { hashtags: [...current, normalized], error: null };
}

/** Splits a pasted list on commas and whitespace so several hashtags can be added at once. */
export function splitNicheHashtagInput(raw: string): string[] {
  return raw
    .split(/[\s,]+/)
    .map((part) => part.trim())
    .filter((part) => part.length > 0);
}
