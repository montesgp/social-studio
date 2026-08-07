"use client";

import { useEffect, type RefObject } from "react";

/**
 * Closes a floating panel when the pointer goes down anywhere outside it, and on `Escape`.
 *
 * The ref must wrap BOTH the trigger and the panel. If it wrapped only the panel, a click on the
 * trigger would be "outside": this handler would close the panel and the trigger's own onClick would
 * immediately reopen it, so the control would appear dead.
 *
 * Uses `pointerdown` rather than `click` so the panel closes on press, before a click lands on
 * whatever is underneath.
 */
export function useDismissOnOutsideClick(
  ref: RefObject<HTMLElement | null>,
  open: boolean,
  onDismiss: () => void,
): void {
  useEffect(() => {
    if (!open) return;

    const onPointerDown = (event: PointerEvent) => {
      const container = ref.current;
      // `composedPath` rather than `event.target`: inside a shadow root the target is retargeted to
      // the host, which would read as "outside" for anything rendered within one.
      if (!container || event.composedPath().includes(container)) return;
      onDismiss();
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onDismiss();
    };

    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown, true);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [ref, open, onDismiss]);
}
