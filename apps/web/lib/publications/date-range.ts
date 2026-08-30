/** ISO 8601 UTC date-range boundary, matching the `fromUtc`/`toUtc` query params (design §9). */
export interface DateRange {
  fromUtc: string;
  toUtc: string;
}

/**
 * The Bank tab's default range (design OQ-4): a UI-weighted last-7-days window, not a query
 * constraint — the repository still supports the full history, this is only the picker default.
 */
export function lastSevenDaysRange(now: Date = new Date()): DateRange {
  const toUtc = now.toISOString();
  const from = new Date(now.getTime());
  from.setUTCDate(from.getUTCDate() - 7);
  return { fromUtc: from.toISOString(), toUtc };
}
