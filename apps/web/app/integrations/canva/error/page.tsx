"use client";

import { useEffect, useState } from "react";

/**
 * Canva OAuth error page — rendered INSIDE the OAuth popup. Signals the failure (with the reason
 * from the callback's ?reason= query) to the opener and closes. Public route.
 */
export default function CanvaErrorPage() {
  const [reason, setReason] = useState<string>("unknown");

  useEffect(() => {
    const r = new URLSearchParams(window.location.search).get("reason") ?? "unknown";
    setReason(r);
    try {
      window.opener?.postMessage({ type: "canva-oauth-result", status: "error", reason: r }, "*");
    } catch {
      // ignore
    }
    window.close();
  }, []);

  return (
    <main className="flex min-h-screen items-center justify-center p-6 text-center">
      <div className="space-y-2">
        <p className="text-base font-medium">Couldn&apos;t connect Canva</p>
        <p className="text-sm text-muted-foreground">Reason: {reason}. You can close this window.</p>
      </div>
    </main>
  );
}
