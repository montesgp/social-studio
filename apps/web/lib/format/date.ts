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

function parseDate(isoString: string): Date | null {
  if (!isoString) {
    return null;
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
