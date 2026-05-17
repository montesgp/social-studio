import { apiRequest } from "@/lib/api/client";
import { ThemeKey, UserPreference } from "@/lib/api/types";

export interface UpdateUserPreferencePayload {
  themeKey: ThemeKey;
  accentTheme?: string | null;
}

export async function getCurrentUserPreferences(token: string) {
  return apiRequest<UserPreference>("/api/me/preferences", undefined, token);
}

export async function updateCurrentUserPreferences(payload: UpdateUserPreferencePayload, token: string) {
  return apiRequest<UserPreference>(
    "/api/me/preferences",
    {
      method: "PUT",
      body: JSON.stringify({
        themeKey: payload.themeKey,
        accentTheme: payload.accentTheme ?? null
      })
    },
    token
  );
}
