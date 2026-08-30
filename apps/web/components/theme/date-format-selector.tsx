"use client";

import { useTheme } from "@/components/theme/theme-provider";
import type { DateFormat } from "@/lib/api/types";

const DATE_FORMAT_OPTIONS: DateFormat[] = ["dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"];

export function DateFormatSelector() {
  const { dateFormat, setDateFormat, isSaving } = useTheme();

  return (
    <select
      aria-label="Date format"
      value={dateFormat}
      disabled={isSaving}
      onChange={(event) => {
        void setDateFormat(event.target.value as DateFormat);
      }}
      className="h-8 rounded-full border border-border bg-background px-2 text-xs text-muted-foreground"
    >
      {DATE_FORMAT_OPTIONS.map((option) => (
        <option key={option} value={option}>
          {option}
        </option>
      ))}
    </select>
  );
}
