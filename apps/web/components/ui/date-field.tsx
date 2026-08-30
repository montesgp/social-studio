"use client";

import { useEffect, useRef, useState } from "react";
import { CalendarDays } from "lucide-react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { datePatternHint, formatUserDate, parseUserDate } from "@/lib/format/date";
import { useDateFormatter } from "@/lib/format/use-date-formatter";

interface DateFieldProps {
  /** Calendar date as `yyyy-MM-dd`. */
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  className?: string;
  "aria-label"?: string;
}

/**
 * A date input that displays and accepts dates in the pattern the user configured in their profile.
 *
 * A native `<input type="date">` always renders in the BROWSER's locale — there is no attribute, CSS
 * or API to override it — so a user who set `dd/MM/yyyy` was still shown `MM/dd/yyyy`. This owns the
 * text representation instead, and keeps a native picker alongside it (hidden, opened from the
 * calendar button) so the calendar UI and keyboard/mobile affordances are not lost.
 */
export function DateField({ value, onChange, disabled, className, ...rest }: DateFieldProps) {
  const { dateFormat } = useDateFormatter();
  const [text, setText] = useState(() => (value ? formatUserDate(value, dateFormat) : ""));
  const pickerRef = useRef<HTMLInputElement | null>(null);

  // Re-sync when the value changes from the outside (a filter reset) or the user changes their
  // pattern preference. Typing does not go through here: `text` is the source of truth mid-edit.
  useEffect(() => {
    setText(value ? formatUserDate(value, dateFormat) : "");
  }, [value, dateFormat]);

  function commit(nextText: string) {
    const parsed = parseUserDate(nextText, dateFormat);
    if (parsed) {
      onChange(parsed);
      return;
    }

    // Unparseable input is never pushed upstream — it would blank or corrupt the filter. Snap the
    // field back to the last good value so the user can see what it reverted to.
    setText(value ? formatUserDate(value, dateFormat) : "");
  }

  return (
    <div className={cn("relative", className)}>
      <Input
        {...rest}
        value={text}
        disabled={disabled}
        inputMode="numeric"
        placeholder={datePatternHint(dateFormat)}
        onChange={(e) => setText(e.target.value)}
        onBlur={(e) => commit(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            commit((e.target as HTMLInputElement).value);
          }
        }}
        className="pr-9"
      />

      <button
        type="button"
        disabled={disabled}
        aria-label="Open calendar"
        className="absolute inset-y-0 right-0 grid w-9 place-items-center text-muted-foreground hover:text-foreground disabled:opacity-50"
        onClick={() => {
          const picker = pickerRef.current;
          if (!picker) return;
          // showPicker() is not universally available; clicking the input is the fallback.
          if (typeof picker.showPicker === "function") {
            picker.showPicker();
          } else {
            picker.click();
          }
        }}
      >
        <CalendarDays className="h-4 w-4" />
      </button>

      {/* The native picker stays in the DOM purely as the calendar UI. It is never the visible text,
          which is what let the browser locale override the user's pattern in the first place. */}
      <input
        ref={pickerRef}
        type="date"
        tabIndex={-1}
        aria-hidden="true"
        disabled={disabled}
        value={value}
        onChange={(e) => e.target.value && onChange(e.target.value)}
        className="pointer-events-none absolute bottom-0 right-2 h-0 w-0 opacity-0"
      />
    </div>
  );
}
