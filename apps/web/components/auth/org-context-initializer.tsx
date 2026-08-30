"use client";

import { useEffect } from "react";
import { getActiveOrganizationId, setActiveOrganizationId } from "@/lib/auth/client-session";
import { getAccessToken } from "@/lib/auth/client-session";

export function OrgContextInitializer() {
  useEffect(() => {
    if (getActiveOrganizationId()) return;

    const token = getAccessToken();
    if (!token) return;

    fetch("/hive-api/api/organizations?page=1&pageSize=1", {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    })
      .then((r) => (r.ok ? r.json() : null))
      .then((data: { items?: { id: string }[] } | null) => {
        const firstId = data?.items?.[0]?.id;
        if (firstId) setActiveOrganizationId(firstId);
      })
      .catch(() => undefined);
  }, []);

  return null;
}
