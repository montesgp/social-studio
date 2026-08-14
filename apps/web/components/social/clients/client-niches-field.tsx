"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { NICHE_DEFAULT_PAGE_SIZE, listNiches } from "@/lib/api/niches";
import { getNicheIcon } from "@/lib/niche-icons";
import type { Niche } from "@/lib/api/types";
import { SearchablePillSelect, type PillOption } from "@/components/ui/searchable-pill-select";

interface ClientNichesFieldProps {
  selected: Niche[];
  onChange: (next: Niche[]) => void;
  token?: string;
  disabled?: boolean;
  loading?: boolean;
}

function toOption(niche: Niche): PillOption {
  return { value: niche.id, label: niche.displayName, description: niche.code };
}

/**
 * Binds the generic searchable pill select to the niche catalog.
 *
 * The generic component knows nothing about niches — this wrapper owns the fetching, the catalog
 * wording and the icon rendering, so any other entity can reuse the control unchanged.
 */
export function ClientNichesField({
  selected,
  onChange,
  token,
  disabled = false,
  loading = false,
}: ClientNichesFieldProps) {
  const [catalog, setCatalog] = useState<Map<string, Niche>>(new Map());
  const [defaultOptions, setDefaultOptions] = useState<PillOption[]>([]);

  const remember = useCallback((niches: Niche[]) => {
    setCatalog((current) => {
      const next = new Map(current);
      for (const niche of niches) next.set(niche.id, niche);
      return next;
    });
  }, []);

  // The default set is whatever the API returns with no search term: the newest entries first.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const page = await listNiches({ page: 1, pageSize: NICHE_DEFAULT_PAGE_SIZE }, token);
        if (cancelled) return;
        remember(page.items);
        setDefaultOptions(page.items.map(toOption));
      } catch {
        if (!cancelled) setDefaultOptions([]);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [token, remember]);

  const search = useCallback(
    async (searchTerm: string): Promise<PillOption[]> => {
      const page = await listNiches(
        { page: 1, pageSize: NICHE_DEFAULT_PAGE_SIZE, searchTerm },
        token,
      );
      remember(page.items);
      return page.items.map(toOption);
    },
    [token, remember],
  );

  const selectedOptions = useMemo(() => selected.map(toOption), [selected]);

  const handleChange = useCallback(
    (next: PillOption[]) => {
      const resolved = next
        .map((option) => catalog.get(option.value) ?? selected.find((n) => n.id === option.value))
        .filter((niche): niche is Niche => Boolean(niche));
      onChange(resolved);
    },
    [catalog, selected, onChange],
  );

  return (
    <SearchablePillSelect
      id="panel-client-niches"
      label={
        <>
          Niches <span className="text-xs text-muted-foreground">(from the platform catalog)</span>
        </>
      }
      selected={selectedOptions}
      defaultOptions={defaultOptions}
      onSearch={search}
      onChange={handleChange}
      disabled={disabled || loading}
      placeholder={loading ? "Loading current niches…" : "Search the niche catalog…"}
      notFoundMessage="No niche matches that search. Niches cannot be invented here — ask a platform admin to add it to the niche catalog first."
      emptyCatalogMessage="The niche catalog has no entries yet."
      helpText="A client may carry several niches. Their curated hashtags are what discovery searches on. Saved with the client."
      renderOptionIcon={(option) => {
        const niche = catalog.get(option.value) ?? selected.find((n) => n.id === option.value);
        const Icon = getNicheIcon(niche?.iconKey ?? null);
        return <Icon className="h-4 w-4" />;
      }}
    />
  );
}
