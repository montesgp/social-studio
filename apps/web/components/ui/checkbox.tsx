import * as React from "react";
import { cn } from "@/lib/utils";

export type CheckboxProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Checkbox = React.forwardRef<HTMLInputElement, CheckboxProps>(({ className, ...props }, ref) => (
  <input
    {...props}
    ref={ref}
    type="checkbox"
    className={cn(
      "h-4 w-4 rounded border-border bg-background text-primary",
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary",
      "disabled:cursor-not-allowed disabled:opacity-50",
      className
    )}
  />
));

Checkbox.displayName = "Checkbox";
