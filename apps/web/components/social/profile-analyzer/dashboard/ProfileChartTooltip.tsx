"use client";

import type { TooltipProps } from "recharts";

type TooltipValue = string | number;

interface ProfileChartTooltipProps extends TooltipProps<TooltipValue, string> {
  valueFormatter?: (value: TooltipValue, name: string) => string;
  nameFormatter?: (name: string) => string;
}

export function ProfileChartTooltip({
  active,
  payload,
  label,
  valueFormatter,
  nameFormatter,
}: ProfileChartTooltipProps) {
  if (!active || !payload || payload.length === 0) {
    return null;
  }

  return (
    <div className="min-w-36 rounded-lg border border-border bg-popover px-3 py-2 text-popover-foreground shadow-lg">
      {label ? (
        <p className="mb-1 text-xs font-semibold text-foreground">{label}</p>
      ) : null}
      <div className="space-y-1">
        {payload.map((item, index) => {
          const name = String(item.name ?? item.dataKey ?? "");
          const value = item.value as TooltipValue;
          const displayName = nameFormatter ? nameFormatter(name) : name;
          const displayValue = valueFormatter
            ? valueFormatter(value, name)
            : String(value);

          return (
            <div key={`${name}-${index}`} className="flex items-center justify-between gap-4">
              <span className="flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
                <span
                  className="h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: (item.payload as Record<string, string> | undefined)?.fill ?? item.color ?? "hsl(var(--primary))" }}
                />
                <span className="truncate">{displayName}</span>
              </span>
              <span className="text-xs font-semibold text-foreground">{displayValue}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
