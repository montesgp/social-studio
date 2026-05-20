"use client";

import { createContext, useContext, useEffect, useState } from "react";
import { getAccessToken, getActiveOrganizationId, setActiveOrganizationId } from "@/lib/auth/client-session";
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
    return { orgId: stored, isInitialized: stored !== null };
  });

  useEffect(() => {
    const syncFromStorage = () => {
      const current = getActiveOrganizationId();
      setState({ orgId: current, isInitialized: true });
    };

    window.addEventListener(ORG_CONTEXT_CHANGE_EVENT, syncFromStorage);

    if (!getActiveOrganizationId()) {
      const token = getAccessToken();
      if (!token) {
        setState({ orgId: null, isInitialized: true });
        return () => window.removeEventListener(ORG_CONTEXT_CHANGE_EVENT, syncFromStorage);
      }

      fetch("/hive-api/api/organizations?page=1&pageSize=1", {
        headers: { Authorization: `Bearer ${token}` },
        cache: "no-store",
      })
        .then((r) => (r.ok ? r.json() : null))
        .then((data: { items?: { id: string }[] } | null) => {
          const firstId = data?.items?.[0]?.id;
          if (firstId) {
            setActiveOrganizationId(firstId);
          } else {
            setState({ orgId: null, isInitialized: true });
          }
        })
        .catch(() => setState({ orgId: null, isInitialized: true }));
    }

    return () => window.removeEventListener(ORG_CONTEXT_CHANGE_EVENT, syncFromStorage);
  }, []);

  return <ActiveOrgContext.Provider value={state}>{children}</ActiveOrgContext.Provider>;
}
