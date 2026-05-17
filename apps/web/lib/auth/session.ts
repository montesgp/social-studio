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

export const ACCESS_TOKEN_COOKIE_NAME = "incoders_hive_access_token";
export const REFRESH_TOKEN_COOKIE_NAME = "incoders_hive_refresh_token";
export const ACCESS_TOKEN_EXPIRES_COOKIE_NAME = "incoders_hive_access_token_expires_at";

export const AUTH_SESSION_STORAGE_KEY = "incoders.hive.auth.session";
export const AUTH_SESSION_CHANGE_EVENT = "incoders-hive-auth-session-changed";
