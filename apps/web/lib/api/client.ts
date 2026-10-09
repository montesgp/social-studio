import { ApiError, ProblemDetails } from "@/lib/api/types";
import * as httpLoadingStore from "@/lib/loading/http-loading-store";
import { ACTIVE_ORG_STORAGE_KEY } from "@/lib/auth/session";
import { refreshAuthSession } from "@/lib/auth/refresh-session";

const BROWSER_API_BASE_URL = "/hive-api";

const AUTH_REFRESH_PATH = "/api/auth/refresh";

function getServerApiBaseUrl() {
  const configuredBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!configuredBaseUrl) {
    throw new Error("NEXT_PUBLIC_API_BASE_URL must be configured for server-side API requests.");
  }

  return configuredBaseUrl.endsWith("/") ? configuredBaseUrl.slice(0, -1) : configuredBaseUrl;
}

export async function apiRequest<T>(path: string, init?: RequestInit, token?: string, tenantId?: string): Promise<T> {
  const headers = new Headers(init?.headers);
  const isFormDataBody =
    typeof FormData !== "undefined" &&
    init?.body !== undefined &&
    init.body instanceof FormData;

  if (init?.body !== undefined && init.body !== null && !isFormDataBody && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const isBrowser = typeof window !== "undefined";
  const baseUrl = isBrowser ? BROWSER_API_BASE_URL : getServerApiBaseUrl();

  if (isBrowser && !headers.has("X-Tenant-Id")) {
    // Only fall back to the globally "active" org (localStorage) when the caller did not already put
    // an explicit X-Tenant-Id on the request (e.g. via `organizationHeaders(organizationId)`). An
    // explicit header always wins — callers that resolve their own org (a platform-admin org picker,
    // a resumed workflow scoped to a specific org) must not be silently redirected to whatever org
    // happens to be "active" elsewhere in the app.
    const orgId = tenantId ?? window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
    if (orgId) headers.set("X-Tenant-Id", orgId);
  }

  const sendRequest = async () => {
    if (isBrowser) httpLoadingStore.increment();
    try {
      return await fetch(`${baseUrl}${path}`, {
        ...init,
        headers,
        cache: "no-store"
      });
    } finally {
      if (isBrowser) {
        httpLoadingStore.decrement();
      }
    }
  };

  let response = await sendRequest();

  // On an expired/invalid access token, transparently rotate the session once and
  // retry the original request. Refresh requests themselves are never retried to
  // avoid loops. A failed refresh clears the session, surfacing the 401 to the caller.
  const canAttemptRefresh = isBrowser && Boolean(token) && !path.startsWith(AUTH_REFRESH_PATH);
  if (response.status === 401 && canAttemptRefresh) {
    const refreshedToken = await refreshAuthSession();
    if (refreshedToken) {
      headers.set("Authorization", `Bearer ${refreshedToken}`);
      response = await sendRequest();
    }
  }

  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      // ignored
    }

    const validationErrors = problem?.errors;
    const validationMessage =
      validationErrors && typeof validationErrors === "object"
        ? Object.values(validationErrors as Record<string, unknown>)
            .flatMap((value) => (Array.isArray(value) ? value : [value]))
            .filter((value): value is string => typeof value === "string")
            .join(" ")
        : "";

    const error: ApiError = {
      message: validationMessage || problem?.detail || problem?.title || `HTTP ${response.status}`,
      problem
    };

    throw error;
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
