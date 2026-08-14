/**
 * Entity-agnostic decision logic behind the searchable pill multi-select.
 *
 * Nothing here knows what a niche is. The component that consumes it receives a search callback
 * and an option list; this module only decides what may still be offered, what the listbox should
 * say, how the keyboard moves, and how a burst of keystrokes collapses into one request.
 */

export interface PillOption {
  /** Stable identity. For catalog-backed entities this is the entity id, not the label. */
  value: string;
  /** What the operator reads in the pill and in the listbox row. */
  label: string;
  /** Optional secondary line (a code, a hint, a count). */
  description?: string;
}

/** Long enough that typing a word does not fire a request per character. */
export const DEFAULT_SEARCH_DEBOUNCE_MS = 300;

/**
 * What the listbox is currently able to say.
 *
 * `no-matches` and `all-selected` are deliberately distinct: the first means the entity does not
 * exist and must be created in its catalog, the second means it exists and is already picked.
 */
export type ListboxStatus =
  | "loading"
  | "options"
  | "no-matches"
  | "all-selected"
  | "empty-catalog";

export interface ListboxStatusInput {
  /** A search request is in flight. */
  isSearching: boolean;
  /** The operator has typed a (normalized) search term. */
  hasSearchTerm: boolean;
  /** How many options the last fetch returned, before selected ones were removed. */
  fetchedCount: number;
  /** How many options survive after removing the ones already selected. */
  selectableCount: number;
}

/** Trims and collapses whitespace so `"real   estate "` and `"real estate"` are one query. */
export function normalizeSearchTerm(raw: string): string {
  return raw.trim().replace(/\s+/g, " ");
}

/**
 * The options the listbox may still offer: de-duplicated, minus everything already selected,
 * in the order the search callback returned them (the backend owns the ordering, not the UI).
 */
export function selectableOptions(
  options: readonly PillOption[],
  selected: readonly PillOption[],
): PillOption[] {
  const taken = new Set(selected.map((option) => option.value));
  const result: PillOption[] = [];

  for (const option of options) {
    if (taken.has(option.value)) continue;
    taken.add(option.value);
    result.push(option);
  }

  return result;
}

export function resolveListboxStatus({
  isSearching,
  hasSearchTerm,
  fetchedCount,
  selectableCount,
}: ListboxStatusInput): ListboxStatus {
  if (isSearching) return "loading";
  if (selectableCount > 0) return "options";
  if (fetchedCount > 0) return "all-selected";
  return hasSearchTerm ? "no-matches" : "empty-catalog";
}

/**
 * Appends a selection. Returns the same reference when nothing changes so React consumers can
 * skip a render. `maxSelected` of `undefined` means unbounded.
 */
export function addSelection(
  selected: readonly PillOption[],
  option: PillOption,
  maxSelected?: number,
): PillOption[] {
  if (selected.some((existing) => existing.value === option.value)) {
    return selected as PillOption[];
  }

  if (typeof maxSelected === "number" && selected.length >= maxSelected) {
    return selected as PillOption[];
  }

  return [...selected, option];
}

/** Removes one selection by value. Returns the same reference when the value was not present. */
export function removeSelection(selected: readonly PillOption[], value: string): PillOption[] {
  if (!selected.some((existing) => existing.value === value)) {
    return selected as PillOption[];
  }

  return selected.filter((existing) => existing.value !== value);
}

/**
 * Wrapping keyboard movement over the listbox. `-1` means "nothing active": moving down from
 * there lands on the first option, moving up lands on the last. An index left over from a longer
 * result set is clamped back into range rather than pointing at nothing.
 */
export function nextActiveIndex(current: number, count: number, direction: 1 | -1): number {
  if (count <= 0) return -1;

  if (current < 0 || current >= count) {
    return direction === 1 ? 0 : count - 1;
  }

  return (current + direction + count) % count;
}

export interface DebouncedRunner<TArgs extends unknown[]> {
  /** Schedules the callback, replacing any call still pending. */
  run: (...args: TArgs) => void;
  /** Drops the pending call without running it. */
  cancel: () => void;
  /** Runs the pending call now, if there is one. */
  flush: () => void;
}

/**
 * Collapses a burst of calls into one, carrying the most recent arguments. A zero delay still
 * defers past the current keystroke, so the caller never sees a synchronous re-entrant run.
 */
export function createDebouncedRunner<TArgs extends unknown[]>(
  callback: (...args: TArgs) => void,
  delayMs: number = DEFAULT_SEARCH_DEBOUNCE_MS,
): DebouncedRunner<TArgs> {
  let timer: ReturnType<typeof setTimeout> | null = null;
  let pending: TArgs | null = null;

  function cancel() {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    pending = null;
  }

  function flush() {
    if (pending === null) return;
    const args = pending;
    cancel();
    callback(...args);
  }

  function run(...args: TArgs) {
    if (timer !== null) clearTimeout(timer);
    pending = args;
    timer = setTimeout(flush, Math.max(0, delayMs));
  }

  return { run, cancel, flush };
}
