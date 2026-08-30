import { type NextRequest, NextResponse } from "next/server";
import {
  ACCESS_TOKEN_COOKIE_NAME,
  ACCESS_TOKEN_EXPIRES_COOKIE_NAME,
  REFRESH_TOKEN_COOKIE_NAME
} from "@/lib/auth/session";
import { resolveAuthRouteDecision } from "@/lib/auth/middleware-guard";

interface SessionValidation {
  isAuthenticated: boolean;
  isPlatformAdmin: boolean;
}

async function validateSession(request: NextRequest, accessToken: string | undefined): Promise<SessionValidation> {
  if (!accessToken) {
    return { isAuthenticated: false, isPlatformAdmin: false };
  }

  try {
    const response = await fetch(new URL("/hive-api/api/me", request.url), {
      method: "GET",
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: "no-store"
    });

    if (!response.ok) {
      return { isAuthenticated: false, isPlatformAdmin: false };
    }

    const payload = (await response.json()) as Partial<{ isPlatformAdmin: boolean }>;
    return {
      isAuthenticated: true,
      isPlatformAdmin: payload.isPlatformAdmin === true
    };
  } catch {
    return { isAuthenticated: false, isPlatformAdmin: false };
  }
}

function clearSessionCookies(response: NextResponse) {
  response.cookies.delete(ACCESS_TOKEN_COOKIE_NAME);
  response.cookies.delete(REFRESH_TOKEN_COOKIE_NAME);
  response.cookies.delete(ACCESS_TOKEN_EXPIRES_COOKIE_NAME);
}

export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (pathname.startsWith("/hive-api")) {
    return NextResponse.next();
  }

  const accessToken = request.cookies.get(ACCESS_TOKEN_COOKIE_NAME)?.value;
  const session = await validateSession(request, accessToken);

  const decision = resolveAuthRouteDecision({
    pathname,
    isAuthenticated: session.isAuthenticated,
    isPlatformAdmin: session.isPlatformAdmin
  });

  if (decision.kind === "redirect") {
    const response = NextResponse.redirect(new URL(decision.location, request.url));
    if (decision.clearSession) {
      clearSessionCookies(response);
    }
    return response;
  }

  if (!session.isAuthenticated && accessToken) {
    const response = NextResponse.next();
    clearSessionCookies(response);
    return response;
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!hive-api|_next/static|_next/image|favicon.ico|.*\\..*).*)"]
};
