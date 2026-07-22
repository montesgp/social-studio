interface CompositionEnvelope {
  markup?: string;
  flattenedImageRef?: string | null;
  [key: string]: unknown;
}

const SRC_ATTRIBUTE = /src\s*=\s*"([^"]*)"/gi;

function isFetchable(reference: string): boolean {
  return /^(https?:|data:)/i.test(reference);
}

/**
 * Resolves a Content Bank item's preview thumbnail from its (already server-resolved)
 * `visualSpecMarkup`, mirroring exactly what the Content Studio's `DesignCanvas` shows:
 * 1. `flattenedImageRef`, when present (a fully-composited slide render).
 * 2. Otherwise, the first already-fetchable (`data:`/`http(s):`) `<img src>` inlined into the
 *    envelope's `markup` — this is how a resolved AI background reaches the client
 *    (`StudioVisualMarkupResolver` inlines it as a data-URI directly into the markup, without ever
 *    setting `flattenedImageRef`), so a grid that only reads `flattenedImageRef` misses it.
 * 3. `null` when neither is resolvable (e.g. an un-rendered draft) — callers should fall back to a
 *    graceful non-image preview rather than a broken image icon.
 */
export function resolveBankThumbnail(visualSpecMarkup: string | null): string | null {
  if (!visualSpecMarkup) return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(visualSpecMarkup);
  } catch {
    return null;
  }

  const envelope = (Array.isArray(parsed) ? parsed[0] : parsed) as CompositionEnvelope | undefined;
  if (!envelope) return null;

  if (typeof envelope.flattenedImageRef === "string" && envelope.flattenedImageRef.length > 0) {
    return envelope.flattenedImageRef;
  }

  const markup = typeof envelope.markup === "string" ? envelope.markup : "";
  for (const match of markup.matchAll(SRC_ATTRIBUTE)) {
    const reference = match[1];
    if (reference && isFetchable(reference)) {
      return reference;
    }
  }

  return null;
}
