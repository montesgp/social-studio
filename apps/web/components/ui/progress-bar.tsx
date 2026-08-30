"use client";

import { useSyncExternalStore } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Sparkles, CheckCircle2 } from "lucide-react";
import { subscribe, getSnapshot } from "@/lib/loading/progress-store";

// Stable reference required by useSyncExternalStore — a new object on every call causes an infinite loop.
const SERVER_SNAPSHOT = {
  isVisible: false,
  value: 0,
  label: "",
  sublabel: undefined as string | undefined,
};

export function ProgressBar(): React.ReactElement {
  const state = useSyncExternalStore(subscribe, getSnapshot, () => SERVER_SNAPSHOT);

  const { isVisible, value, label, sublabel } = state;
  const isComplete = value >= 100;

  return (
    <AnimatePresence>
      {isVisible && (
        <motion.div
          key="progress-bar"
          initial={{ opacity: 0, y: 20, scale: 0.95 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: 10, scale: 0.98, transition: { duration: 0.25, ease: "easeOut" } }}
          transition={{ type: "spring", stiffness: 260, damping: 20 }}
          style={{
            position: "fixed",
            bottom: "1.5rem",
            right: "1.5rem",
            zIndex: 9998,
            width: "18rem",
            borderRadius: "1rem",
            padding: "1.25rem",
          }}
          className="border border-border bg-card/80 shadow-xl shadow-black/30 backdrop-blur-md"
        >
          {/* Header row */}
          <div className="flex items-center gap-2">
            {isComplete ? (
              <CheckCircle2 size={14} className="shrink-0 text-primary" />
            ) : (
              <Sparkles size={14} className="shrink-0 text-primary" />
            )}
            <span className="text-sm font-medium text-foreground">{label}</span>
          </div>

          {/* Progress track */}
          <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-muted">
            <motion.div
              className="h-full rounded-full"
              style={{
                background:
                  "linear-gradient(90deg, hsl(var(--accent-start)), hsl(var(--accent-end)))",
                boxShadow:
                  "0 0 8px hsl(var(--accent-start) / 0.6), 0 0 16px hsl(var(--accent-end) / 0.3)",
              }}
              animate={{ width: `${value}%` }}
              transition={{ type: "spring", stiffness: 60, damping: 15 }}
            />
          </div>

          {/* Footer row */}
          <div className="mt-2 flex items-center justify-between">
            {sublabel ? (
              <span className="text-xs text-muted-foreground">{sublabel}</span>
            ) : (
              <span />
            )}
            <span className="font-mono text-xs text-primary">{Math.round(value)}%</span>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
