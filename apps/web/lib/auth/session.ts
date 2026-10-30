export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
}

export interface StoredAuthSession {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
}

export const ACCESS_TOKEN_COOKIE_NAME = "social_studio_access_token";
export const REFRESH_TOKEN_COOKIE_NAME = "social_studio_refresh_token";
export const ACCESS_TOKEN_EXPIRES_COOKIE_NAME = "social_studio_access_token_expires_at";

export const AUTH_SESSION_STORAGE_KEY = "social-studio.auth.session";
export const AUTH_SESSION_CHANGE_EVENT = "social-studio-auth-session-changed";
export const ACTIVE_ORG_STORAGE_KEY = "social-studio.active.org";
export const ORG_CONTEXT_CHANGE_EVENT = "social-studio-org-context-changed";
