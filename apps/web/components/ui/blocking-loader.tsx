"use client";

import { Loader2 } from "lucide-react";
import { motion } from "framer-motion";
import { cn } from "@/lib/utils";

interface BlockingLoaderProps {
  visible: boolean;
  title: string;
  description?: string;
  mode?: "spinner" | "progress";
  className?: string;
}

export function BlockingLoader({
  visible,
  title,
  description,
  mode = "spinner",
  className,
}: BlockingLoaderProps) {
  if (!visible) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={title}
      className={cn(
        "fixed inset-0 z-[70] flex items-center justify-center bg-background/85 backdrop-blur-sm",
        className
      )}
    >
      <div className="w-full max-w-md rounded-2xl border border-border/50 bg-card p-6 shadow-2xl">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-primary/10 p-2">
            <Loader2 className="h-5 w-5 animate-spin text-primary" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-foreground">{title}</h3>
            {description ? (
              <p className="text-xs text-muted-foreground">{description}</p>
            ) : null}
          </div>
        </div>

        {mode === "progress" ? (
          <div className="relative mt-4 h-2 w-full overflow-hidden rounded-full bg-muted">
            <motion.div
              className="absolute inset-y-0 left-0 w-2/5 rounded-full bg-gradient-to-r from-blue-500 via-purple-500 to-primary"
              initial={{ x: "-120%" }}
              animate={{ x: "220%" }}
              transition={{ duration: 1.2, repeat: Infinity, ease: "easeInOut" }}
            />
          </div>
        ) : null}
      </div>
    </div>
  );
}
