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
  // True from the moment the warning fires until an explicit reset ("Stay signed in").
  // While true, user activity is ignored so the auto-logout countdown can lapse instead
  // of being silently cancelled by a stray mousemove.
  const warningActiveRef = useRef(false);
  const onWarningRef = useRef(onWarning);
  const onLogoutRef = useRef(onLogout);

  useEffect(() => { onWarningRef.current = onWarning; }, [onWarning]);
  useEffect(() => { onLogoutRef.current = onLogout; }, [onLogout]);

  const clearTimers = useCallback(() => {
    if (warningTimerRef.current) clearTimeout(warningTimerRef.current);
    if (logoutTimerRef.current) clearTimeout(logoutTimerRef.current);
  }, []);

  const resetTimers = useCallback(() => {
    // Explicit (re)arm: initial mount and "Stay signed in". This is the ONLY path
    // allowed to leave the warning state and revive the session.
    warningActiveRef.current = false;
    clearTimers();
    warningTimerRef.current = setTimeout(() => {
      warningActiveRef.current = true;
      onWarningRef.current();
      logoutTimerRef.current = setTimeout(() => {
        onLogoutRef.current();
      }, logoutAfterMs - warningAfterMs);
    }, warningAfterMs);
  }, [clearTimers, warningAfterMs, logoutAfterMs]);

  useEffect(() => {
    resetTimers();

    const handleActivity = () => {
      // Once the warning is showing, activity must NOT keep the session alive —
      // only an explicit "Stay signed in" may. Otherwise mousemove/scroll events
      // cancel the pending auto-logout and the countdown stalls at 0s forever.
      if (warningActiveRef.current) return;
      resetTimers();
    };
    ACTIVITY_EVENTS.forEach((event) => window.addEventListener(event, handleActivity, { passive: true }));

    return () => {
      clearTimers();
      ACTIVITY_EVENTS.forEach((event) => window.removeEventListener(event, handleActivity));
    };
  }, [resetTimers, clearTimers]);

  return { resetTimers };
}
