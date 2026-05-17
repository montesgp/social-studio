import { type NextRequest, NextResponse } from "next/server";
import { decodeJwtPayload, extractRoles, isTokenExpired } from "@/lib/auth/jwt";
import { ACCESS_TOKEN_COOKIE_NAME } from "@/lib/auth/session";

function isPublicPath(pathname: string) {
  return pathname === "/login" || pathname === "/forgot-password" || pathname === "/reset-password";
}

export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (pathname.startsWith("/hive-api")) {
    return NextResponse.next();
  }

  const accessToken = request.cookies.get(ACCESS_TOKEN_COOKIE_NAME)?.value;
  const payload = accessToken ? decodeJwtPayload(accessToken) : null;
  const roles = payload ? extractRoles(payload) : [];
  const isAuthenticated = Boolean(accessToken && payload && !isTokenExpired(payload));
  const isPlatformAdmin = roles.includes("sysadmin");

  if (!isAuthenticated && !isPublicPath(pathname)) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  if (isAuthenticated && pathname === "/login") {
    return NextResponse.redirect(new URL("/dashboard", request.url));
  }

  if (pathname.startsWith("/admin") && !isPlatformAdmin) {
    return NextResponse.redirect(new URL("/dashboard", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/((?!hive-api|_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp)$).*)"
  ]
};
