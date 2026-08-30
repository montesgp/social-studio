import * as React from "react";
import { cn } from "@/lib/utils";

export interface SelectProps extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, "size"> {
  /** "default" = h-9 text-sm px-3 (form fields, panels)
   *  "sm"      = h-8 text-xs px-2 (inline toolbar controls) */
  size?: "default" | "sm";
}

export const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  ({ className, size = "default", ...props }, ref) => (
    <select
      ref={ref}
      className={cn(
        "rounded-md border border-border bg-background text-foreground",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary",
        "disabled:cursor-not-allowed disabled:opacity-50",
        size === "sm"
          ? "h-8 px-2 text-xs"
          : "h-9 px-3 text-sm",
        className,
      )}
      {...props}
    />
  ),
);

Select.displayName = "Select";
