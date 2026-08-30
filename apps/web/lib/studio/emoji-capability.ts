/**
 * Gates the slide-text emoji picker on a proven render-side capability (R6.2/R6.4 — Content Studio
 * editing completeness). The picker MUST NOT be offered until `supportsColorEmoji` is confirmed —
 * "not loaded yet" and "confirmed false" both mean "do not offer it", never a default of true. This
 * is the structural guarantee that no exported slide ever ships a tofu glyph.
 */

export interface RenderCapabilitySignal {
  supportsColorEmoji: boolean;
}

export function canOfferSlideEmojiPicker(capabilities: RenderCapabilitySignal | null): boolean {
  return capabilities?.supportsColorEmoji === true;
}
