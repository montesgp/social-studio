"use client";

import { useEffect, useState } from "react";
import { listFormatCatalog } from "@/lib/api/content-generation";
import { mapSelectableFormatOptions, type SelectableFormatOption } from "@/lib/social/format-catalog";

/**
 * Server-driven format catalog for the Generator's per-candidate format picker. Replaces the static
 * `ALL_FORMAT_KEYS` union (which wrongly exposed auxiliary formats — content-idea, cta-variation,
 * caption, hook) with the backend's filtered catalog, same as single-analyzer's own picker.
 * Best-effort: a failed fetch leaves the picker empty rather than throwing.
 */
export function useFormatCatalog(token: string | null | undefined): SelectableFormatOption[] {
  const [options, setOptions] = useState<SelectableFormatOption[]>([]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;

    listFormatCatalog({ token })
      .then((entries) => {
        if (!cancelled) setOptions(mapSelectableFormatOptions(entries));
      })
      .catch(() => {
        /* non-fatal: picker stays empty until the next mount/token change retries */
      });

    return () => {
      cancelled = true;
    };
  }, [token]);

  return options;
}
