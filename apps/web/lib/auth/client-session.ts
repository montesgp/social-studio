"use client";

import {
  ACCESS_TOKEN_COOKIE_NAME,
  ACCESS_TOKEN_EXPIRES_COOKIE_NAME,
  ACTIVE_ORG_STORAGE_KEY,
  AUTH_SESSION_CHANGE_EVENT,
  AUTH_SESSION_STORAGE_KEY,
  REFRESH_TOKEN_COOKIE_NAME,
  StoredAuthSession,
  TokenResponse
} from "@/lib/auth/session";

function isBrowser() {
  return typeof window !== "undefined";
}

function cookieOptions(maxAgeSeconds?: number) {
  if (!isBrowser()) {
    return "";
  }

  const secure = window.location.protocol === "https:" ? "; Secure" : "";
  const maxAge = typeof maxAgeSeconds === "number" ? `; Max-Age=${Math.max(0, Math.floor(maxAgeSeconds))}` : "";
  return `; Path=/; SameSite=Lax${secure}${maxAge}`;
}

function setCookie(name: string, value: string, maxAgeSeconds?: number) {
  if (!isBrowser()) {
    return;
  }

  document.cookie = `${name}=${encodeURIComponent(value)}${cookieOptions(maxAgeSeconds)}`;
}

function removeCookie(name: string) {
  if (!isBrowser()) {
    return;
  }

  document.cookie = `${name}=; Path=/; Max-Age=0; SameSite=Lax`;
}

function publishSessionChange() {
  if (!isBrowser()) {
    return;
  }

  window.dispatchEvent(new CustomEvent(AUTH_SESSION_CHANGE_EVENT));
}

function readSessionFromStorage(): StoredAuthSession | null {
  if (!isBrowser()) {
    return null;
  }

  const raw = window.localStorage.getItem(AUTH_SESSION_STORAGE_KEY);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<StoredAuthSession>;
    if (
      typeof parsed.accessToken !== "string" ||
      typeof parsed.refreshToken !== "string" ||
      typeof parsed.accessTokenExpiresAtUtc !== "string"
    ) {
      return null;
    }

    return {
      accessToken: parsed.accessToken,
      refreshToken: parsed.refreshToken,
      accessTokenExpiresAtUtc: parsed.accessTokenExpiresAtUtc
    };
  } catch {
    return null;
  }
}

export function readAuthSession(): StoredAuthSession | null {
  return readSessionFromStorage();
}

export function getAccessToken(): string {
  const session = readSessionFromStorage();
  if (!session) {
    return "";
  }

  const expiresAt = Date.parse(session.accessTokenExpiresAtUtc);
  if (Number.isFinite(expiresAt) && expiresAt <= Date.now() + 30_000) {
    return "";
  }

  return session.accessToken;
}

export function saveAuthSession(tokens: TokenResponse) {
  if (!isBrowser()) {
    return;
  }

  const session: StoredAuthSession = {
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    accessTokenExpiresAtUtc: tokens.accessTokenExpiresAtUtc
  };

  window.localStorage.setItem(AUTH_SESSION_STORAGE_KEY, JSON.stringify(session));

  const accessTokenExpiry = Date.parse(tokens.accessTokenExpiresAtUtc);
  const accessTokenMaxAge = Number.isFinite(accessTokenExpiry)
    ? Math.max(0, Math.floor((accessTokenExpiry - Date.now()) / 1000))
    : undefined;
  const refreshTokenMaxAge = 60 * 60 * 24 * 30;

  setCookie(ACCESS_TOKEN_COOKIE_NAME, tokens.accessToken, accessTokenMaxAge);
  setCookie(REFRESH_TOKEN_COOKIE_NAME, tokens.refreshToken, refreshTokenMaxAge);
  setCookie(ACCESS_TOKEN_EXPIRES_COOKIE_NAME, tokens.accessTokenExpiresAtUtc, accessTokenMaxAge);

  publishSessionChange();
}

export function clearAuthSession() {
  if (!isBrowser()) {
    return;
  }

  window.localStorage.removeItem(AUTH_SESSION_STORAGE_KEY);
  window.localStorage.removeItem(ACTIVE_ORG_STORAGE_KEY);
  removeCookie(ACCESS_TOKEN_COOKIE_NAME);
  removeCookie(REFRESH_TOKEN_COOKIE_NAME);
  removeCookie(ACCESS_TOKEN_EXPIRES_COOKIE_NAME);
  publishSessionChange();
}

export function getActiveOrganizationId(): string | null {
  if (!isBrowser()) return null;
  return window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
}

export function setActiveOrganizationId(id: string): void {
  if (!isBrowser()) return;
  window.localStorage.setItem(ACTIVE_ORG_STORAGE_KEY, id);
}

export function clearActiveOrganizationId(): void {
  if (!isBrowser()) return;
  window.localStorage.removeItem(ACTIVE_ORG_STORAGE_KEY);
}

export function subscribeToSessionChanges(listener: (session: StoredAuthSession | null) => void) {
  if (!isBrowser()) {
    return () => undefined;
  }

  const handler = () => listener(readSessionFromStorage());

  window.addEventListener(AUTH_SESSION_CHANGE_EVENT, handler);
  window.addEventListener("storage", handler);

  return () => {
    window.removeEventListener(AUTH_SESSION_CHANGE_EVENT, handler);
    window.removeEventListener("storage", handler);
  };
}
