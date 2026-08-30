"use client";

import { createContext, useContext, useEffect, useState } from "react";
import {
  clearActiveOrganizationId,
  getAccessToken,
  getActiveOrganizationId,
  setActiveOrganizationId
} from "@/lib/auth/client-session";
import { ORG_CONTEXT_CHANGE_EVENT } from "@/lib/auth/session";

interface ActiveOrgState {
  orgId: string | null;
  isInitialized: boolean;
}

const ActiveOrgContext = createContext<ActiveOrgState>({ orgId: null, isInitialized: false });

export function useActiveOrg(): ActiveOrgState {
  return useContext(ActiveOrgContext);
}

export function ActiveOrgProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<ActiveOrgState>(() => {
    const stored = typeof window !== "undefined" ? getActiveOrganizationId() : null;
    return { orgId: stored, isInitialized: false };
  });

  useEffect(() => {
    const syncFromStorage = () => {
      const current = getActiveOrganizationId();
      setState({ orgId: current, isInitialized: true });
    };

    window.addEventListener(ORG_CONTEXT_CHANGE_EVENT, syncFromStorage);

    const token = getAccessToken();
    if (!token) {
      setState({ orgId: null, isInitialized: true });
      return () => window.removeEventListener(ORG_CONTEXT_CHANGE_EVENT, syncFromStorage);
    }

    let cancelled = false;
    void fetch("/hive-api/api/me/organizations", {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    })
      .then((r) => (r.ok ? r.json() : null))
      .then((data: { id: string }[] | null) => {
        if (cancelled) return;

        const organizations = data ?? [];
        const stored = getActiveOrganizationId();
        const hasStored = !!stored && organizations.some((organization) => organization.id === stored);
        const nextOrgId = hasStored ? stored : organizations[0]?.id ?? null;

        if (nextOrgId) {
          if (nextOrgId !== stored) {
            setActiveOrganizationId(nextOrgId);
          } else {
            setState({ orgId: nextOrgId, isInitialized: true });
          }
          return;
        }

        if (stored) {
          clearActiveOrganizationId();
        } else {
          setState({ orgId: null, isInitialized: true });
        }
      })
      .catch(() => {
        if (cancelled) return;
        setState({ orgId: getActiveOrganizationId(), isInitialized: true });
      });

    return () => {
      cancelled = true;
      window.removeEventListener(ORG_CONTEXT_CHANGE_EVENT, syncFromStorage);
    };
  }, []);

  return <ActiveOrgContext.Provider value={state}>{children}</ActiveOrgContext.Provider>;
}
