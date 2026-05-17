export type JwtPayload = Record<string, unknown>;

const ROLE_CLAIM_KEYS = [
  "role",
  "roles",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
];

const EMAIL_CLAIM_KEYS = [
  "email",
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"
];

function base64UrlDecode(input: string): string | null {
  try {
    const normalized = input.replace(/-/g, "+").replace(/_/g, "/");
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");

    if (typeof window !== "undefined" && typeof window.atob === "function") {
      return decodeURIComponent(
        Array.from(window.atob(padded))
          .map((char) => `%${char.charCodeAt(0).toString(16).padStart(2, "0")}`)
          .join("")
      );
    }

    return Buffer.from(padded, "base64").toString("utf-8");
  } catch {
    return null;
  }
}

export function decodeJwtPayload(token: string): JwtPayload | null {
  const parts = token.split(".");
  if (parts.length < 2) {
    return null;
  }

  const decoded = base64UrlDecode(parts[1]);
  if (!decoded) {
    return null;
  }

  try {
    return JSON.parse(decoded) as JwtPayload;
  } catch {
    return null;
  }
}

export function extractRoles(payload: JwtPayload): string[] {
  const roles = new Set<string>();

  for (const key of ROLE_CLAIM_KEYS) {
    const value = payload[key];

    if (typeof value === "string" && value.trim().length > 0) {
      roles.add(value.trim());
      continue;
    }

    if (Array.isArray(value)) {
      for (const item of value) {
        if (typeof item === "string" && item.trim().length > 0) {
          roles.add(item.trim());
        }
      }
    }
  }

  return Array.from(roles);
}

export function extractEmail(payload: JwtPayload): string | null {
  for (const key of EMAIL_CLAIM_KEYS) {
    const value = payload[key];
    if (typeof value === "string" && value.trim().length > 0) {
      return value.trim();
    }
  }

  return null;
}

export function isTokenExpired(payload: JwtPayload, skewSeconds = 30): boolean {
  const exp = payload.exp;
  if (typeof exp !== "number") {
    return false;
  }

  const expiresAtMs = exp * 1000;
  return expiresAtMs <= Date.now() + skewSeconds * 1000;
}
