import type { ContentFormatKey, FormatCatalogEntry } from "@/lib/api/content-generation";

/** One user-selectable format offered by the Generator's format-composition picker. */
export interface SelectableFormatOption {
  formatKey: ContentFormatKey;
  label: string;
}

/**
 * Maps server format-catalog entries (`GET /api/social/content-generation/formats`) into the Generator's
 * selectable format list. The backend already filters out auxiliary formats (content-idea, cta-variation,
 * caption, hook — auto-bundled into their parent deliverable at generation time, e.g. feed-single comes
 * with caption+hashtags), but this stays defensive and filters again so a backend regression cannot leak
 * an auxiliary format back into the picker.
 */
export function mapSelectableFormatOptions(entries: FormatCatalogEntry[]): SelectableFormatOption[] {
  return entries
    .filter((entry) => !entry.isAuxiliary)
    .map((entry) => ({
      formatKey: entry.formatKey as ContentFormatKey,
      label: entry.displayLabel
    }));
}
