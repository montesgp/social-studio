import type { ReactNode } from "react";
import { CheckCircle2, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export interface OptionCardSelectorOption {
  value: string;
  label: string;
  description?: string;
  icon?: ReactNode;
  disabled?: boolean;
}

interface OptionCardSelectorProps {
  label: string;
  value: string;
  options: OptionCardSelectorOption[];
  onChange: (value: string) => void;
  noneOption?: OptionCardSelectorOption;
  loading?: boolean;
  loadingLabel?: string;
  emptyLabel?: string;
  selectionLabel?: string;
  className?: string;
  optionsGridClassName?: string;
}

export function OptionCardSelector({
  label,
  value,
  options,
  onChange,
  noneOption,
  loading = false,
  loadingLabel = "Loading...",
  emptyLabel = "No options available",
  selectionLabel = "Single selection",
  className,
  optionsGridClassName,
}: OptionCardSelectorProps) {
  const visibleOptions = noneOption ? [noneOption, ...options] : options;

  return (
    <div className={cn("space-y-2", className)}>
      <div className="flex items-center justify-between gap-3">
        <label className="text-xs font-medium text-muted-foreground">{label}</label>
        {value ? <span className="text-[10px] text-muted-foreground/70">{selectionLabel}</span> : null}
      </div>
      <div className={cn(
        "grid max-h-44 grid-cols-1 gap-2 overflow-y-auto rounded-md border border-border bg-background/40 p-2 sm:grid-cols-2",
        optionsGridClassName
      )}>
        {loading ? (
          <Button type="button" variant="outline" disabled className="h-auto min-h-14 justify-start px-3 py-2 text-left">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            <span className="text-xs">{loadingLabel}</span>
          </Button>
        ) : visibleOptions.length === 0 ? (
          <Button type="button" variant="outline" disabled className="h-auto min-h-14 justify-start px-3 py-2 text-left">
            <span className="text-xs">{emptyLabel}</span>
          </Button>
        ) : (
          visibleOptions.map((option) => {
            const selected = value === option.value;

            return (
              <Button
                key={option.value || "__none"}
                type="button"
                variant={selected ? "secondary" : "outline"}
                aria-pressed={selected}
                onClick={() => onChange(option.value)}
                disabled={option.disabled}
                className={cn(
                  "h-auto min-h-14 justify-start px-3 py-2 text-left",
                  selected && "border-primary/60 bg-primary/10"
                )}
              >
                {option.icon ? (
                  <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md border border-border bg-background">
                    {option.icon}
                  </span>
                ) : null}
                <span className="min-w-0">
                  <span className="block truncate text-xs font-medium">{option.label}</span>
                  {option.description ? (
                    <span className="block truncate text-[10px] font-normal text-muted-foreground">
                      {option.description}
                    </span>
                  ) : null}
                </span>
                {selected ? <CheckCircle2 className="ml-auto h-3.5 w-3.5 shrink-0 text-primary" /> : null}
              </Button>
            );
          })
        )}
      </div>
    </div>
  );
}
