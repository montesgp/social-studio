import type { DateFormat } from "@/lib/api/types";

/**
 * Renders an ISO timestamp using the user's configured date pattern.
 * Deterministic string substitution — does NOT rely on `Intl` locale for the date part,
 * since the pattern is an explicit user choice, not a locale inference.
 */
export function formatUserDate(isoString: string, pattern: DateFormat): string {
  const date = parseDate(isoString);
  if (!date) {
    return "";
  }

  return renderDatePattern(date, pattern);
}

/**
 * Renders an ISO timestamp as `{formatUserDate} HH:mm` (24h), using the user's configured pattern.
 */
export function formatUserDateTime(isoString: string, pattern: DateFormat): string {
  const date = parseDate(isoString);
  if (!date) {
    return "";
  }

  const datePart = renderDatePattern(date, pattern);
  const hours = pad(date.getHours());
  const minutes = pad(date.getMinutes());
  return `${datePart} ${hours}:${minutes}`;
}

/**
 * Renders only the time part as `HH:mm` (24h). The user's pattern governs the DATE layout and says
 * nothing about time, but the 24h choice is shared with {@link formatUserDateTime} so a timestamp
 * never switches between 24h and AM/PM depending on which component renders it.
 */
export function formatUserTime(isoString: string): string {
  const date = parseDate(isoString);
  if (!date) {
    return "";
  }

  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/**
 * Parses text the user typed in THEIR configured pattern back into a `yyyy-MM-dd` calendar date.
 * Returns null when the text is incomplete or not a real date (e.g. `31/02/2026`).
 *
 * The inverse of {@link formatUserDate}. Needed because a native `<input type="date">` renders in the
 * BROWSER's locale and offers no way to override it, so a user who configured `dd/MM/yyyy` was shown
 * `MM/dd/yyyy` regardless — the one place the preference was impossible to honour without owning the
 * text representation ourselves.
 */
export function parseUserDate(text: string, pattern: DateFormat): string | null {
  const parts = text.split(/[^0-9]+/).filter((part) => part.length > 0);
  if (parts.length !== 3) {
    return null;
  }

  let day: string;
  let month: string;
  let year: string;

  switch (pattern) {
    case "dd/MM/yyyy":
      [day, month, year] = parts;
      break;
    case "MM/dd/yyyy":
      [month, day, year] = parts;
      break;
    case "yyyy-MM-dd":
      [year, month, day] = parts;
      break;
    default:
      return null;
  }

  if (year.length !== 4) {
    return null;
  }

  const yearNumber = Number(year);
  const monthNumber = Number(month);
  const dayNumber = Number(day);

  // Round-trip through Date to reject impossible calendar dates: JS rolls 31/02 over to 03/03, so a
  // date that comes back with different components was never valid in the first place.
  const candidate = new Date(yearNumber, monthNumber - 1, dayNumber);
  if (
    candidate.getFullYear() !== yearNumber ||
    candidate.getMonth() !== monthNumber - 1 ||
    candidate.getDate() !== dayNumber
  ) {
    return null;
  }

  return `${year}-${pad(monthNumber)}-${pad(dayNumber)}`;
}

/** Placeholder/hint text for a date field in the user's pattern (e.g. `dd/mm/yyyy`). */
export function datePatternHint(pattern: DateFormat): string {
  return pattern.toLowerCase();
}

function parseDate(isoString: string): Date | null {
  if (!isoString) {
    return null;
  }

  // A bare `yyyy-MM-dd` is a CALENDAR date, not an instant. `new Date("2026-08-06")` parses it as
  // midnight UTC per the spec, and every renderer below reads local getters — so west of Greenwich
  // the value came back as the PREVIOUS day (06/08 rendered as 05/08 at UTC-3). Constructing it
  // from local components keeps the calendar day the user typed.
  const calendarDate = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoString);
  if (calendarDate) {
    const [, year, month, day] = calendarDate;
    return new Date(Number(year), Number(month) - 1, Number(day));
  }

  const date = new Date(isoString);
  return Number.isNaN(date.getTime()) ? null : date;
}

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

function renderDatePattern(date: Date, pattern: DateFormat): string {
  const dd = pad(date.getDate());
  const mm = pad(date.getMonth() + 1);
  const yyyy = String(date.getFullYear());

  switch (pattern) {
    case "dd/MM/yyyy":
      return `${dd}/${mm}/${yyyy}`;
    case "MM/dd/yyyy":
      return `${mm}/${dd}/${yyyy}`;
    case "yyyy-MM-dd":
      return `${yyyy}-${mm}-${dd}`;
    default:
      return "";
  }
}
