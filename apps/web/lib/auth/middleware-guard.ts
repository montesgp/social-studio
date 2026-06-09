export interface AuthRouteDecisionInput {
  pathname: string;
  isAuthenticated: boolean;
  isPlatformAdmin: boolean;
}

export type AuthRouteDecision =
  | { kind: "allow" }
  | { kind: "redirect"; location: "/login" | "/dashboard"; clearSession: boolean };

export function isPublicPath(pathname: string) {
  return (
    pathname === "/login" ||
    pathname === "/forgot-password" ||
    pathname === "/reset-password" ||
    // Canva OAuth popup result pages: rendered inside the OAuth popup on the 127.0.0.1 origin,
    // which has no session cookie. They only postMessage the result to the opener and close, so
    // they must not be auth-guarded (otherwise the popup bounces to /login).
    pathname.startsWith("/integrations/canva/")
  );
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
