"use client";

import { useEffect, useLayoutEffect, useState } from "react";
import { getAccessToken, subscribeToSessionChanges } from "@/lib/auth/client-session";

// `useLayoutEffect` warns ("does nothing on the server") when it runs during SSR. This file is a
// client component, but Next still server-renders it once for the initial HTML, so fall back to
// `useEffect` there — the ternary is evaluated once per environment at module load, not per
// render, so this stays a plain, unconditional hook call from React's point of view.
const useIsomorphicLayoutEffect = typeof window !== "undefined" ? useLayoutEffect : useEffect;

export function useAccessToken() {
  const [accessToken, setAccessToken] = useState<string>("");

  // Resolve the real token synchronously, before the browser paints and before any OTHER hook's
  // own `useEffect` can observe the placeholder "". A plain `useEffect` here left a real gap: on
  // mount, this hook's effect and a consumer's data-fetch effect (e.g. useSocialNetworks, declared
  // later in the same component) both run in the SAME initial passive-effect pass, so the consumer
  // saw the stale "" first, read it as "no session", and set its own `loading` to false — then
  // flipped back to `true` a render later once the real token arrived and its effect re-ran. That
  // produced a visible double-loader flash on first navigation into a page after login: the page's
  // "everything ready" check saw the token resolved and the consumer's stale `loading=false` at
  // the same instant, briefly hid its loading overlay, then had to reopen it once the real fetch
  // actually started. Running this in a layout effect resolves the token before any passive effect
  // fires, so no consumer ever observes the placeholder.
  useIsomorphicLayoutEffect(() => {
    setAccessToken(getAccessToken());
    return subscribeToSessionChanges(() => {
      setAccessToken(getAccessToken());
    });
  }, []);

  return accessToken;
}
