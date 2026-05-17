"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { CheckCircle2, CircleAlert, Info, X } from "lucide-react";
import { cn } from "@/lib/utils";

type ToastTone = "success" | "error" | "info";

interface ToastPayload {
  title: string;
  description?: string;
  tone?: ToastTone;
  durationMs?: number;
}

interface ToastMessage extends Required<Pick<ToastPayload, "title" | "tone" | "durationMs">> {
  id: string;
  description?: string;
}

interface ToastContextValue {
  show: (payload: ToastPayload) => void;
  success: (title: string, description?: string) => void;
  error: (title: string, description?: string) => void;
  info: (title: string, description?: string) => void;
}

const ToastContext = createContext<ToastContextValue | undefined>(undefined);

function toneStyles(tone: ToastTone) {
  switch (tone) {
    case "success":
      return "border-emerald-500/50 bg-emerald-500/10 text-emerald-700 dark:text-emerald-100";
    case "error":
      return "border-red-500/50 bg-red-500/10 text-red-700 dark:text-red-100";
    default:
      return "border-primary/40 bg-primary/10 text-foreground";
  }
}

function ToneIcon({ tone }: { tone: ToastTone }) {
  if (tone === "success") return <CheckCircle2 className="h-4 w-4" />;
  if (tone === "error") return <CircleAlert className="h-4 w-4" />;
  return <Info className="h-4 w-4" />;
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const timers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

  const dismiss = useCallback((id: string) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
    if (timers.current[id]) {
      clearTimeout(timers.current[id]);
      delete timers.current[id];
    }
  }, []);

  const show = useCallback(
    (payload: ToastPayload) => {
      const id = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
      const message: ToastMessage = {
        id,
        title: payload.title,
        description: payload.description,
        tone: payload.tone ?? "info",
        durationMs: payload.durationMs ?? 4500
      };

      setToasts((current) => [...current, message]);
      timers.current[id] = setTimeout(() => dismiss(id), message.durationMs);
    },
    [dismiss]
  );

  useEffect(() => {
    return () => {
      Object.values(timers.current).forEach((timer) => clearTimeout(timer));
      timers.current = {};
    };
  }, []);

  const value = useMemo<ToastContextValue>(
    () => ({
      show,
      success: (title, description) => show({ title, description, tone: "success" }),
      error: (title, description) => show({ title, description, tone: "error" }),
      info: (title, description) => show({ title, description, tone: "info" })
    }),
    [show]
  );

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-full max-w-sm flex-col gap-2">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            role="status"
            aria-live="polite"
            className={cn(
              "pointer-events-auto rounded-xl border bg-card px-3 py-2 shadow-xl backdrop-blur",
              toneStyles(toast.tone)
            )}
          >
            <div className="flex items-start gap-2">
              <span className="mt-0.5 shrink-0">
                <ToneIcon tone={toast.tone} />
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-sm font-medium">{toast.title}</p>
                {toast.description ? <p className="mt-0.5 text-xs text-muted-foreground">{toast.description}</p> : null}
              </div>
              <button
                type="button"
                aria-label="Close notification"
                className="text-muted-foreground transition-colors hover:text-foreground"
                onClick={() => dismiss(toast.id)}
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error("useToast must be used within ToastProvider");
  }

  return context;
}
