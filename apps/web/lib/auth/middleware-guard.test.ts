import assert from "node:assert/strict";
import test from "node:test";
import { isPublicPath, resolveAuthRouteDecision } from "./middleware-guard.ts";

test("isPublicPath recognizes auth public routes", () => {
  assert.equal(isPublicPath("/login"), true);
  assert.equal(isPublicPath("/forgot-password"), true);
  assert.equal(isPublicPath("/reset-password"), true);
  assert.equal(isPublicPath("/dashboard"), false);
});

test("resolveAuthRouteDecision redirects unauthenticated protected requests to login and clears session", () => {
  const decision = resolveAuthRouteDecision({
    pathname: "/dashboard",
    isAuthenticated: false,
    isPlatformAdmin: false
  });

  assert.deepEqual(decision, {
    kind: "redirect",
    location: "/login",
    clearSession: true
  });
});

test("resolveAuthRouteDecision keeps unauthenticated users on public routes", () => {
  const decision = resolveAuthRouteDecision({
    pathname: "/login",
    isAuthenticated: false,
    isPlatformAdmin: false
  });

  assert.deepEqual(decision, { kind: "allow" });
});

test("resolveAuthRouteDecision redirects authenticated users away from login", () => {
  const decision = resolveAuthRouteDecision({
    pathname: "/login",
    isAuthenticated: true,
    isPlatformAdmin: false
  });

  assert.deepEqual(decision, {
    kind: "redirect",
    location: "/dashboard",
    clearSession: false
  });
});

test("resolveAuthRouteDecision blocks non-admin users from admin routes", () => {
  const decision = resolveAuthRouteDecision({
    pathname: "/admin/organizations",
    isAuthenticated: true,
    isPlatformAdmin: false
  });

  assert.deepEqual(decision, {
    kind: "redirect",
    location: "/dashboard",
    clearSession: false
  });
});

test("resolveAuthRouteDecision allows admin users on admin routes", () => {
  const decision = resolveAuthRouteDecision({
    pathname: "/admin/organizations",
    isAuthenticated: true,
    isPlatformAdmin: true
  });

  assert.deepEqual(decision, { kind: "allow" });
});
