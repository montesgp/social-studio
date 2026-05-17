import { cookies } from "next/headers";
import { decodeJwtPayload, extractEmail, extractRoles, isTokenExpired } from "@/lib/auth/jwt";
import { ACCESS_TOKEN_COOKIE_NAME, REFRESH_TOKEN_COOKIE_NAME } from "@/lib/auth/session";

export interface ServerAuthSession {
  accessToken: string | null;
  refreshToken: string | null;
  email: string | null;
  roles: string[];
  isAuthenticated: boolean;
  isExpired: boolean;
  isPlatformAdmin: boolean;
}

export async function getServerAuthSession(): Promise<ServerAuthSession> {
  const cookieStore = await cookies();
  const accessToken = cookieStore.get(ACCESS_TOKEN_COOKIE_NAME)?.value ?? null;
  const refreshToken = cookieStore.get(REFRESH_TOKEN_COOKIE_NAME)?.value ?? null;

  if (!accessToken) {
    return {
      accessToken: null,
      refreshToken,
      email: null,
      roles: [],
      isAuthenticated: false,
      isExpired: false,
      isPlatformAdmin: false
    };
  }

  const payload = decodeJwtPayload(accessToken);
  if (!payload) {
    return {
      accessToken,
      refreshToken,
      email: null,
      roles: [],
      isAuthenticated: false,
      isExpired: true,
      isPlatformAdmin: false
    };
  }

  const roles = extractRoles(payload);
  const expired = isTokenExpired(payload);

  return {
    accessToken,
    refreshToken,
    email: extractEmail(payload),
    roles,
    isAuthenticated: !expired,
    isExpired: expired,
    isPlatformAdmin: roles.includes("sysadmin")
  };
}
