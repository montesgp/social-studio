"use client";

import { clearAuthSession, readAuthSession, saveAuthSession } from "@/lib/auth/client-session";
import { TokenResponse } from "@/lib/auth/session";

const REFRESH_ENDPOINT = "/hive-api/api/auth/refresh";

let inFlightRefresh: Promise<string> | null = null;

async function performRefresh(): Promise<string> {
  const session = readAuthSession();
  if (!session?.refreshToken) {
    clearAuthSession();
    return "";
  }

  try {
    const response = await fetch(REFRESH_ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: session.refreshToken }),
      cache: "no-store"
    });

    if (!response.ok) {
      clearAuthSession();
      return "";
    }

    const tokens = (await response.json()) as TokenResponse;
    saveAuthSession(tokens);
    return tokens.accessToken;
  } catch {
    clearAuthSession();
    return "";
  }
}

/**
 * Rotates the access/refresh token pair using the stored refresh token.
 * Concurrent callers share a single in-flight request to avoid refresh stampedes.
 * Returns the new access token, or an empty string when the session could not be
 * renewed (in which case the local session is cleared).
 */
export function refreshAuthSession(): Promise<string> {
  if (inFlightRefresh) {
    return inFlightRefresh;
  }

  inFlightRefresh = performRefresh().finally(() => {
    inFlightRefresh = null;
  });

  return inFlightRefresh;
}
