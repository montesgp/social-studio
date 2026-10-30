import assert from "node:assert/strict";
import { test } from "node:test";
import { apiRequest } from "./client.ts";

/**
 * BUG 2 (frontend): `apiRequest` unconditionally overwrote an already-explicit `X-Tenant-Id` header
 * with whatever org happens to be "active" in localStorage. Every browser caller that resolves its own
 * org id explicitly (e.g. `getContentWorkflowHistory(organizationId, ...)` via
 * `organizationHeaders(organizationId)`) had that value silently discarded whenever it diverged from
 * the localStorage-active org — the workflow history panel's `selectedOrganizationId` can diverge from
 * the app-wide "active org" (platform-admin org picker, a resumed workflow scoped to a specific org),
 * while `/workflow/progress/[workflowId]` always uses the active-org value directly and so never showed
 * the symptom. This test stubs a minimal browser (`window`/`localStorage`/`fetch`) to exercise that
 * browser-only header-resolution branch under the Node test runner.
 */

const ACTIVE_ORG_STORAGE_KEY = "social-studio.active.org";

function withStubbedBrowser<T>(activeOrgId: string | null, run: () => Promise<T>): Promise<T> {
  const store = new Map<string, string>();
  if (activeOrgId !== null) {
    store.set(ACTIVE_ORG_STORAGE_KEY, activeOrgId);
  }

  const fakeWindow = {
    localStorage: {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => store.set(key, value),
      removeItem: (key: string) => store.delete(key)
    }
  };

  (globalThis as Record<string, unknown>).window = fakeWindow;

  return run().finally(() => {
    delete (globalThis as Record<string, unknown>).window;
  });
}

test("apiRequest keeps an explicit X-Tenant-Id header instead of the localStorage-active org", async () => {
  await withStubbedBrowser("active-org-from-storage", async () => {
    let capturedTenantHeader: string | null = null;
    const originalFetch = globalThis.fetch;
    globalThis.fetch = (async (_input: unknown, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      capturedTenantHeader = headers.get("X-Tenant-Id");
      return new Response(JSON.stringify({ ok: true }), { status: 200 });
    }) as typeof fetch;

    try {
      await apiRequest("/api/social/workflows/abc/history", {
        headers: { "X-Tenant-Id": "explicit-workflow-org" }
      });
    } finally {
      globalThis.fetch = originalFetch;
    }

    assert.equal(capturedTenantHeader, "explicit-workflow-org");
  });
});

test("apiRequest falls back to the localStorage-active org when no explicit header is given", async () => {
  await withStubbedBrowser("active-org-from-storage", async () => {
    let capturedTenantHeader: string | null = null;
    const originalFetch = globalThis.fetch;
    globalThis.fetch = (async (_input: unknown, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      capturedTenantHeader = headers.get("X-Tenant-Id");
      return new Response(JSON.stringify({ ok: true }), { status: 200 });
    }) as typeof fetch;

    try {
      await apiRequest("/api/social/workflows/abc/history", {});
    } finally {
      globalThis.fetch = originalFetch;
    }

    assert.equal(capturedTenantHeader, "active-org-from-storage");
  });
});
