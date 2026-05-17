"use client";

import { useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import { clearAuthSession } from "@/lib/auth/client-session";
import { useIdleSessionTimeout } from "@/components/auth/use-idle-session-timeout";
import { SessionTimeoutModal } from "@/components/auth/session-timeout-modal";

const WARNING_AFTER_MS = 25 * 60 * 1000;
const LOGOUT_AFTER_MS = 30 * 60 * 1000;
const COUNTDOWN_SECONDS = (LOGOUT_AFTER_MS - WARNING_AFTER_MS) / 1000;

export function IdleSessionProvider({ children }: { children: React.ReactNode }) {
  const [warningOpen, setWarningOpen] = useState(false);
  const router = useRouter();

  const handleSignOut = useCallback(async () => {
    setWarningOpen(false);
    clearAuthSession();
    router.push("/login");
  }, [router]);

  const { resetTimers } = useIdleSessionTimeout({
    warningAfterMs: WARNING_AFTER_MS,
    logoutAfterMs: LOGOUT_AFTER_MS,
    onWarning: () => setWarningOpen(true),
    onLogout: handleSignOut,
  });

  const handleStay = useCallback(() => {
    setWarningOpen(false);
    resetTimers();
  }, [resetTimers]);

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
