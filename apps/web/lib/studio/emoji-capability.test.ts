import { test } from "node:test";
import assert from "node:assert/strict";
import { canOfferSlideEmojiPicker } from "./emoji-capability.ts";

test("canOfferSlideEmojiPicker: true when the render path proved color-emoji support", () => {
  assert.equal(canOfferSlideEmojiPicker({ supportsColorEmoji: true }), true);
});

test("canOfferSlideEmojiPicker: false when capability is unproven (R6.2 gate)", () => {
  assert.equal(canOfferSlideEmojiPicker({ supportsColorEmoji: false }), false);
});

test("canOfferSlideEmojiPicker: false when capabilities have not loaded yet (never ship tofu, R6.4)", () => {
  assert.equal(canOfferSlideEmojiPicker(null), false);
});
