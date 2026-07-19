/**
 * Pure caption-limit math for the Content Studio caption UI (R5.3). Mirrors the domain invariant
 * enforced server-side by `Caption.Create` (Instagram's 2,200-character / 30-hashtag ceilings) so
 * the operator gets live feedback instead of a silent server rejection.
 */

export const CAPTION_MAX_LENGTH = 2200;
export const CAPTION_MAX_HASHTAGS = 30;

const HASHTAG_PATTERN = /#\w+/g;

export interface CaptionLimits {
  length: number;
  hashtagCount: number;
  isEmpty: boolean;
  isOverLength: boolean;
  isOverHashtags: boolean;
}

export function computeCaptionLimits(text: string): CaptionLimits {
  const trimmed = text.trim();
  const hashtagCount = (text.match(HASHTAG_PATTERN) ?? []).length;

  return {
    length: text.length,
    hashtagCount,
    isEmpty: trimmed.length === 0,
    isOverLength: text.length > CAPTION_MAX_LENGTH,
    isOverHashtags: hashtagCount > CAPTION_MAX_HASHTAGS,
  };
}
