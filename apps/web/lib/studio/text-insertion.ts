/**
 * Pure caret-relative text insertion, used to insert an emoji into the caption textarea at the
 * cursor position rather than always appending at the end (R6.1). Kept DOM-free so it is unit
 * testable; the caller reads `selectionStart`/`selectionEnd` from the real `<textarea>` and passes
 * the caret index in.
 */
export interface CaretInsertion {
  text: string;
  caretIndex: number;
}

export function insertTextAtCaret(text: string, insertion: string, caretIndex: number): CaretInsertion {
  const clamped = Math.max(0, Math.min(caretIndex, text.length));
  const nextText = text.slice(0, clamped) + insertion + text.slice(clamped);
  return { text: nextText, caretIndex: clamped + insertion.length };
}
