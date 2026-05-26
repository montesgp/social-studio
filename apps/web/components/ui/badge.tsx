import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

const badgeVariants = cva("inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium", {
  variants: {
    variant: {
      default: "border-primary/35 bg-primary/15 text-foreground",
      muted: "border-border bg-muted text-muted-foreground",
      outline: "border-border bg-background text-foreground",
      success:
        "border-emerald-600/35 bg-emerald-100 text-emerald-800 dark:border-emerald-400/40 dark:bg-emerald-500/15 dark:text-emerald-200",
      destructive:
        "border-rose-600/35 bg-rose-100 text-rose-800 dark:border-rose-400/40 dark:bg-rose-500/15 dark:text-rose-200",
      info:
        "border-sky-600/35 bg-sky-100 text-sky-800 dark:border-sky-400/40 dark:bg-sky-500/15 dark:text-sky-200",
      accent:
        "border-cyan-600/35 bg-cyan-100 text-cyan-800 dark:border-cyan-400/40 dark:bg-cyan-500/15 dark:text-cyan-200",
      warning:
        "border-amber-600/35 bg-amber-100 text-amber-900 dark:border-amber-400/40 dark:bg-amber-500/15 dark:text-amber-200"
    }
  },
  defaultVariants: {
    variant: "default"
  }
});

export type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>["variant"]>;

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

export function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />;
}
