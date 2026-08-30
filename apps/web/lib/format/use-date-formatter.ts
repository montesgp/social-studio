"use client";

import { useMemo } from "react";
import { useTheme } from "@/components/theme/theme-provider";
import { formatUserDate, formatUserDateTime, formatUserTime } from "@/lib/format/date";

/**
 * Date/time formatters bound to the date pattern the user chose in their profile.
 *
 * Every surface that renders a date MUST go through these. Raw `toLocaleDateString()` /
 * `Intl.DateTimeFormat(undefined, …)` infer the format from the BROWSER locale, which silently
 * contradicts the user's explicit preference — the same timestamp then read as `08/06/2026` in one
 * panel and `Aug 6` in the next.
 *
 * Returned functions are stable for a given pattern, so they are safe in effect/memo dependencies.
 */
export function useDateFormatter() {
  const { dateFormat } = useTheme();

  return useMemo(
    () => ({
      /** Date only, in the user's pattern. */
      formatDate: (value: string | null | undefined) => (value ? formatUserDate(value, dateFormat) : "—"),
      /** Date + `HH:mm` (24h). */
      formatDateTime: (value: string | null | undefined) => (value ? formatUserDateTime(value, dateFormat) : "—"),
      /** Time only, `HH:mm` (24h) — the date pattern does not apply, but the 24h choice is shared. */
      formatTime: (value: string | null | undefined) => (value ? formatUserTime(value) : "—"),
      dateFormat,
    }),
    [dateFormat],
  );
}
