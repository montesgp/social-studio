"use client";

import { useEffect, useState, useSyncExternalStore } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { subscribe, getSnapshot } from "@/lib/loading/http-loading-store";

type LoaderState = "hidden" | "loading" | "completing";

export function HttpLoader(): React.ReactElement | null {
  const isLoading = useSyncExternalStore(subscribe, getSnapshot, () => false);
  const [loaderState, setLoaderState] = useState<LoaderState>("hidden");

  useEffect(() => {
    if (isLoading && loaderState === "hidden") {
      setLoaderState("loading");
    } else if (!isLoading && loaderState === "loading") {
      setLoaderState("completing");
    }
  }, [isLoading, loaderState]);

  const handleCompletingComplete = () => {
    setLoaderState("hidden");
  };

  return (
    <AnimatePresence>
      {loaderState !== "hidden" && (
        <motion.div
          key="http-loader"
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            height: "3px",
            zIndex: 9999,
            transformOrigin: "left center",
            background:
              "linear-gradient(90deg, hsl(var(--accent-start)), hsl(var(--accent-end)))",
            boxShadow:
              "0 0 8px hsl(var(--accent-start) / 0.6), 0 0 16px hsl(var(--accent-end) / 0.3)",
          }}
          initial={{ scaleX: 0, opacity: 1 }}
          animate={
            loaderState === "loading"
              ? { scaleX: 0.85, opacity: 1 }
              : { scaleX: 1, opacity: 0, y: -2 }
          }
          transition={
            loaderState === "loading"
              ? { duration: 0.4, ease: "easeOut" }
              : { scaleX: { duration: 0.15 }, opacity: { duration: 0.2, delay: 0.15 }, y: { duration: 0.2, delay: 0.15 } }
          }
          onAnimationComplete={() => {
            if (loaderState === "completing") {
              handleCompletingComplete();
            }
          }}
        />
      )}
    </AnimatePresence>
  );
}
