"use client";

import { useEffect } from "react";

/**
 * Canva OAuth success page — rendered INSIDE the OAuth popup (on the 127.0.0.1 origin). It tells the
 * opener window the connection succeeded and closes itself, so the main app (which never navigated
 * away) just refreshes the integration state. Public route (no session needed here).
 */
export default function CanvaConnectedPage() {
  useEffect(() => {
    try {
      window.opener?.postMessage({ type: "canva-oauth-result", status: "connected" }, "*");
    } catch {
      // ignore — opener may be gone; the user can close this window manually.
    }
    window.close();
  }, []);

  return (
    <main className="flex min-h-screen items-center justify-center p-6 text-center">
      <div className="space-y-2">
        <p className="text-base font-medium">Canva connected ✓</p>
        <p className="text-sm text-muted-foreground">You can close this window.</p>
      </div>
    </main>
  );
}
