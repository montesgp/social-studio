"use client";

import { useEffect, useRef, useCallback } from "react";

interface UseIdleSessionTimeoutOptions {
  warningAfterMs: number;
  logoutAfterMs: number;
  onWarning: () => void;
  onLogout: () => void;
}

const ACTIVITY_EVENTS: (keyof WindowEventMap)[] = [
  "mousemove",
  "mousedown",
  "keydown",
  "touchstart",
  "scroll",
  "click",
];

export function useIdleSessionTimeout({
  warningAfterMs,
  logoutAfterMs,
  onWarning,
  onLogout,
}: UseIdleSessionTimeoutOptions) {
  const warningTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const logoutTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const onWarningRef = useRef(onWarning);
  const onLogoutRef = useRef(onLogout);

  useEffect(() => { onWarningRef.current = onWarning; }, [onWarning]);
  useEffect(() => { onLogoutRef.current = onLogout; }, [onLogout]);

  const clearTimers = useCallback(() => {
    if (warningTimerRef.current) clearTimeout(warningTimerRef.current);
    if (logoutTimerRef.current) clearTimeout(logoutTimerRef.current);
  }, []);

  const resetTimers = useCallback(() => {
    clearTimers();
    warningTimerRef.current = setTimeout(() => {
      onWarningRef.current();
      logoutTimerRef.current = setTimeout(() => {
        onLogoutRef.current();
      }, logoutAfterMs - warningAfterMs);
    }, warningAfterMs);
  }, [clearTimers, warningAfterMs, logoutAfterMs]);

  useEffect(() => {
    resetTimers();

    const handleActivity = () => resetTimers();
    ACTIVITY_EVENTS.forEach((event) => window.addEventListener(event, handleActivity, { passive: true }));

    return () => {
      clearTimers();
      ACTIVITY_EVENTS.forEach((event) => window.removeEventListener(event, handleActivity));
    };
  }, [resetTimers, clearTimers]);

  return { resetTimers };
}
