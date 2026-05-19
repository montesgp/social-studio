export interface AuthRouteDecisionInput {
  pathname: string;
  isAuthenticated: boolean;
  isPlatformAdmin: boolean;
}

export type AuthRouteDecision =
  | { kind: "allow" }
  | { kind: "redirect"; location: "/login" | "/dashboard"; clearSession: boolean };

export function isPublicPath(pathname: string) {
  return pathname === "/login" || pathname === "/forgot-password" || pathname === "/reset-password";
}

export function resolveAuthRouteDecision({
  pathname,
  isAuthenticated,
  isPlatformAdmin
}: AuthRouteDecisionInput): AuthRouteDecision {
  if (!isAuthenticated && !isPublicPath(pathname)) {
    return { kind: "redirect", location: "/login", clearSession: true };
  }

  if (isAuthenticated && pathname === "/login") {
    return { kind: "redirect", location: "/dashboard", clearSession: false };
  }

  if (pathname.startsWith("/admin") && !isPlatformAdmin) {
    return { kind: "redirect", location: "/dashboard", clearSession: false };
  }

  return { kind: "allow" };
}
