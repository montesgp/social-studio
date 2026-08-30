"use client";

import { ReactNode, useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Loader2, Search, X } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  DEFAULT_SEARCH_DEBOUNCE_MS,
  addSelection,
  createDebouncedRunner,
  nextActiveIndex,
  normalizeSearchTerm,
  removeSelection,
  resolveListboxStatus,
  selectableOptions,
  type PillOption,
} from "@/lib/ui/pill-multi-select";

export type { PillOption } from "@/lib/ui/pill-multi-select";

export interface SearchablePillSelectProps {
  /** Base id for the input, listbox and options. Must be unique on the page. */
  id: string;
  /** Current selection, rendered as removable pills. The parent owns this state. */
  selected: PillOption[];
  /** Shown before the operator types anything — whatever page the caller considers the default. */
  defaultOptions: PillOption[];
  /**
   * Runs the caller's search. The component never fetches on its own and knows nothing about the
   * entity being searched; it only debounces, de-duplicates and renders what comes back.
   */
  onSearch: (searchTerm: string) => Promise<PillOption[]>;
  onChange: (next: PillOption[]) => void;
  /**
   * Shown when a search matched nothing. Inline creation is not supported by design, so this text
   * should tell the operator where the item has to be created first. Worded by the caller so other
   * entities can phrase it their own way.
   */
  notFoundMessage: ReactNode;
  /** Shown when the caller's default option set is empty and no term has been typed. */
  emptyCatalogMessage?: ReactNode;
  label?: ReactNode;
  helpText?: ReactNode;
  placeholder?: string;
  disabled?: boolean;
  /** Optional cap on how many items may be selected. Unbounded when omitted. */
  maxSelected?: number;
  debounceMs?: number;
  /** Optional leading visual for a pill and its listbox row (an icon, an avatar, a swatch). */
  renderOptionIcon?: (option: PillOption) => ReactNode;
  className?: string;
}

/**
 * Entity-agnostic searchable multi-select: type to search through a caller-provided callback,
 * pick from a listbox, and see the picks as removable pills.
 *
 * Deliberately has no "create <term>" affordance: catalogs that back this control are curated
 * elsewhere, and inventing entries from a form is what the catalog exists to prevent.
 */
export function SearchablePillSelect({
  id,
  selected,
  defaultOptions,
  onSearch,
  onChange,
  notFoundMessage,
  emptyCatalogMessage = "Nothing to choose from yet.",
  label,
  helpText,
  placeholder = "Search…",
  disabled = false,
  maxSelected,
  debounceMs = DEFAULT_SEARCH_DEBOUNCE_MS,
  renderOptionIcon,
  className,
}: SearchablePillSelectProps) {
  const generatedId = useId();
  const baseId = id || generatedId;
  const listboxId = `${baseId}-listbox`;

  const containerRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  /** Guards against an older search resolving after a newer one. */
  const requestSeqRef = useRef(0);

  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);
  const [fetched, setFetched] = useState<PillOption[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [activeIndex, setActiveIndex] = useState(-1);

  const normalizedTerm = normalizeSearchTerm(term);
  const hasSearchTerm = normalizedTerm.length > 0;
  const sourceOptions = hasSearchTerm ? fetched : defaultOptions;
  const options = useMemo(
    () => selectableOptions(sourceOptions, selected),
    [sourceOptions, selected],
  );
  const status = resolveListboxStatus({
    isSearching,
    hasSearchTerm,
    fetchedCount: sourceOptions.length,
    selectableCount: options.length,
  });
  const atCapacity = typeof maxSelected === "number" && selected.length >= maxSelected;

  const runSearch = useCallback(
    async (searchTerm: string) => {
      const seq = ++requestSeqRef.current;
      setIsSearching(true);
      setSearchError(null);
      try {
        const results = await onSearch(searchTerm);
        if (seq !== requestSeqRef.current) return;
        setFetched(results);
      } catch (error) {
        if (seq !== requestSeqRef.current) return;
        setFetched([]);
        setSearchError(error instanceof Error ? error.message : "Search failed.");
      } finally {
        if (seq === requestSeqRef.current) setIsSearching(false);
      }
    },
    [onSearch],
  );

  const debouncedSearch = useMemo(
    () => createDebouncedRunner((searchTerm: string) => void runSearch(searchTerm), debounceMs),
    [runSearch, debounceMs],
  );

  useEffect(() => () => debouncedSearch.cancel(), [debouncedSearch]);

  useEffect(() => {
    if (!hasSearchTerm) {
      // Back to the default set: drop anything in flight so a late response cannot overwrite it.
      debouncedSearch.cancel();
      requestSeqRef.current += 1;
      setIsSearching(false);
      setFetched([]);
      setSearchError(null);
      return;
    }

    debouncedSearch.run(normalizedTerm);
  }, [normalizedTerm, hasSearchTerm, debouncedSearch]);

  // Keep the active option inside the current result set.
  useEffect(() => {
    setActiveIndex((current) => (current >= options.length ? -1 : current));
  }, [options.length]);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  function commit(next: PillOption[]) {
    if (next === selected) return;
    onChange(next);
  }

  function pick(option: PillOption) {
    commit(addSelection(selected, option, maxSelected));
    setTerm("");
    setActiveIndex(-1);
    inputRef.current?.focus();
  }

  function drop(value: string) {
    commit(removeSelection(selected, value));
  }

  function onInputKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => nextActiveIndex(current, options.length, event.key === "ArrowDown" ? 1 : -1));
      return;
    }

    if (event.key === "Enter") {
      if (!open || activeIndex < 0 || activeIndex >= options.length) return;
      event.preventDefault();
      pick(options[activeIndex]);
      return;
    }

    if (event.key === "Escape") {
      if (!open) return;
      event.preventDefault();
      setOpen(false);
      setActiveIndex(-1);
      return;
    }

    if (event.key === "Backspace" && term.length === 0 && selected.length > 0) {
      event.preventDefault();
      drop(selected[selected.length - 1].value);
    }
  }

  return (
    <div className={cn("space-y-1.5", className)} ref={containerRef}>
      {label ? (
        <label htmlFor={baseId} className="text-sm font-medium">
          {label}
        </label>
      ) : null}

      {selected.length > 0 ? (
        <ul className="flex flex-wrap gap-1.5" aria-label="Selected items">
          {selected.map((option) => (
            <li key={option.value}>
              <span className="inline-flex items-center gap-1.5 rounded-full border border-border bg-muted/60 py-1 pl-2.5 pr-1 text-xs font-medium">
                {renderOptionIcon ? (
                  <span className="flex items-center text-muted-foreground">{renderOptionIcon(option)}</span>
                ) : null}
                <span>{option.label}</span>
                <button
                  type="button"
                  disabled={disabled}
                  onClick={() => drop(option.value)}
                  aria-label={`Remove ${option.label}`}
                  className="rounded-full p-0.5 text-muted-foreground transition hover:bg-background hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-50"
                >
                  <X className="h-3 w-3" />
                </button>
              </span>
            </li>
          ))}
        </ul>
      ) : null}

      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
        <input
          ref={inputRef}
          id={baseId}
          type="text"
          role="combobox"
          autoComplete="off"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={open && activeIndex >= 0 ? `${baseId}-option-${activeIndex}` : undefined}
          value={term}
          disabled={disabled || atCapacity}
          placeholder={atCapacity ? `Limit of ${maxSelected} reached` : placeholder}
          onChange={(event) => {
            setTerm(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onInputKeyDown}
          className="h-10 w-full rounded-md border border-border bg-transparent py-2 pl-9 pr-9 text-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-60"
        />
        {isSearching ? (
          <Loader2 className="pointer-events-none absolute right-3 top-3 h-4 w-4 animate-spin text-muted-foreground" />
        ) : null}

        {open && !disabled && !atCapacity ? (
          <div className="absolute z-30 mt-1 w-full overflow-hidden rounded-md border border-border bg-card shadow-xl">
            <ul id={listboxId} role="listbox" aria-label="Search results" className="max-h-64 overflow-y-auto py-1">
              {status === "options" ? (
                options.map((option, index) => (
                  <li
                    key={option.value}
                    id={`${baseId}-option-${index}`}
                    role="option"
                    aria-selected={index === activeIndex}
                    onMouseEnter={() => setActiveIndex(index)}
                    onMouseDown={(event) => {
                      // Keep focus on the input so the listbox does not blur-close before the click.
                      event.preventDefault();
                      pick(option);
                    }}
                    className={cn(
                      "flex cursor-pointer items-center gap-2 px-3 py-2 text-sm",
                      index === activeIndex ? "bg-muted text-foreground" : "text-muted-foreground",
                    )}
                  >
                    {renderOptionIcon ? (
                      <span className="flex shrink-0 items-center">{renderOptionIcon(option)}</span>
                    ) : null}
                    <span className="min-w-0">
                      <span className="block truncate font-medium text-foreground">{option.label}</span>
                      {option.description ? (
                        <span className="block truncate font-mono text-xs opacity-75">{option.description}</span>
                      ) : null}
                    </span>
                  </li>
                ))
              ) : (
                <li className="px-3 py-3 text-sm text-muted-foreground">
                  {searchError
                    ? searchError
                    : status === "loading"
                      ? "Searching…"
                      : status === "no-matches"
                        ? notFoundMessage
                        : status === "all-selected"
                          ? "Every match is already selected."
                          : emptyCatalogMessage}
                </li>
              )}
            </ul>
          </div>
        ) : null}
      </div>

      {helpText ? <p className="text-xs text-muted-foreground">{helpText}</p> : null}
    </div>
  );
}
