"use client";

import { useEffect, useState } from "react";
import { getAccessToken, subscribeToSessionChanges } from "@/lib/auth/client-session";

export function useAccessToken() {
  const [accessToken, setAccessToken] = useState<string>("");

  useEffect(() => {
    setAccessToken(getAccessToken());
    return subscribeToSessionChanges(() => {
      setAccessToken(getAccessToken());
    });
  }, []);

  return accessToken;
}
