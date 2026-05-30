"use client";

import { useState, useCallback, useEffect, useRef } from "react";
import { useRouter } from "next/navigation";
import {
  clearAuthSession,
  readAuthSession,
  subscribeToSessionChanges
} from "@/lib/auth/client-session";
import { refreshAuthSession } from "@/lib/auth/refresh-session";
import { useIdleSessionTimeout } from "@/components/auth/use-idle-session-timeout";
import { SessionTimeoutModal } from "@/components/auth/session-timeout-modal";

const WARNING_AFTER_MS = 25 * 60 * 1000;
const LOGOUT_AFTER_MS = 30 * 60 * 1000;
const COUNTDOWN_SECONDS = (LOGOUT_AFTER_MS - WARNING_AFTER_MS) / 1000;
// Renew the access token shortly before it expires so active users never hit a 401.
const REFRESH_BEFORE_EXPIRY_MS = 60 * 1000;

export function IdleSessionProvider({ children }: { children: React.ReactNode }) {
  const [warningOpen, setWarningOpen] = useState(false);
  const router = useRouter();

  // Mirror the warning state into a ref so the refresh scheduler can read it
  // without re-subscribing on every toggle.
  const warningOpenRef = useRef(warningOpen);
  useEffect(() => {
    warningOpenRef.current = warningOpen;
  }, [warningOpen]);

  const handleSignOut = useCallback(() => {
    setWarningOpen(false);
    clearAuthSession();
    router.push("/login");
  }, [router]);

  const { resetTimers } = useIdleSessionTimeout({
    warningAfterMs: WARNING_AFTER_MS,
    logoutAfterMs: LOGOUT_AFTER_MS,
    onWarning: () => setWarningOpen(true),
    onLogout: handleSignOut
  });

  const handleStay = useCallback(async () => {
    setWarningOpen(false);
    resetTimers();
    // "Stay signed in" must renew the backend session, not just the idle timers.
    await refreshAuthSession();
  }, [resetTimers]);

  // Proactive token refresh: while the user is active, rotate the access token
  // before it expires. When the inactivity warning is showing, we deliberately
  // skip the refresh so the idle-logout path can lapse the session.
  useEffect(() => {
    let refreshTimer: ReturnType<typeof setTimeout> | null = null;

    const scheduleRefresh = () => {
      if (refreshTimer) clearTimeout(refreshTimer);

      const session = readAuthSession();
      if (!session) return;

      const expiresAt = Date.parse(session.accessTokenExpiresAtUtc);
      if (!Number.isFinite(expiresAt)) return;

      const delay = Math.max(0, expiresAt - REFRESH_BEFORE_EXPIRY_MS - Date.now());
      refreshTimer = setTimeout(() => {
        if (warningOpenRef.current) return;
        // A successful refresh publishes a session change, which reschedules below.
        void refreshAuthSession();
      }, delay);
    };

    scheduleRefresh();

    const unsubscribe = subscribeToSessionChanges((session) => {
      if (!session) {
        router.push("/login");
        return;
      }
      scheduleRefresh();
    });

    return () => {
      if (refreshTimer) clearTimeout(refreshTimer);
      unsubscribe();
    };
  }, [router]);

  return (
    <>
      {children}
      <SessionTimeoutModal
        open={warningOpen}
        countdownSeconds={COUNTDOWN_SECONDS}
        onStay={handleStay}
        onSignOut={handleSignOut}
      />
    </>
  );
}
