"use client";

import { createContext, useContext, useEffect, useRef, useState } from "react";
import { ApiError, DateFormat, ThemeKey } from "@/lib/api/types";
import { getCurrentUserPreferences, updateCurrentUserPreferences } from "@/lib/api/user-preferences";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";

const DEFAULT_DATE_FORMAT: DateFormat = "dd/MM/yyyy";

interface ThemeContextValue {
  themeKey: ThemeKey;
  resolvedThemeKey: Exclude<ThemeKey, "system">;
  dateFormat: DateFormat;
  isSaving: boolean;
  setTheme: (nextThemeKey: ThemeKey) => Promise<void>;
  setDateFormat: (nextDateFormat: DateFormat) => Promise<void>;
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

function resolveTheme(themeKey: ThemeKey, prefersDark: boolean): Exclude<ThemeKey, "system"> {
  if (themeKey !== "system") {
    return themeKey;
  }

  return prefersDark ? "dark" : "light";
}

export function ThemeProvider({
  children,
  initialThemeKey,
  initialDateFormat = DEFAULT_DATE_FORMAT
}: {
  children: React.ReactNode;
  initialThemeKey: ThemeKey;
  initialDateFormat?: DateFormat;
}) {
  const toast = useToast();
  const accessToken = useAccessToken();
  const syncErrorNotified = useRef(false);
  const [themeKey, setThemeKey] = useState<ThemeKey>(initialThemeKey);
  const [dateFormat, setDateFormatState] = useState<DateFormat>(initialDateFormat);
  const [prefersDark, setPrefersDark] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const applyPreference = () => setPrefersDark(mediaQuery.matches);
    applyPreference();
    mediaQuery.addEventListener("change", applyPreference);
    return () => mediaQuery.removeEventListener("change", applyPreference);
  }, []);

  const resolvedThemeKey = resolveTheme(themeKey, prefersDark);

  useEffect(() => {
    document.documentElement.dataset.theme = resolvedThemeKey;
  }, [resolvedThemeKey]);

  useEffect(() => {
    let cancelled = false;

    async function syncFromBackend() {
      if (!accessToken) {
        return;
      }

      try {
        const preferences = await getCurrentUserPreferences(accessToken);
        if (!cancelled) {
          setThemeKey(preferences.themeKey);
          setDateFormatState(preferences.dateFormat ?? DEFAULT_DATE_FORMAT);
          syncErrorNotified.current = false;
        }
      } catch (error) {
        if (!syncErrorNotified.current) {
          const message = (error as ApiError).message ?? "Could not load your saved theme preferences.";
          toast.error("Theme sync failed", message);
          syncErrorNotified.current = true;
        }
      }
    }

    void syncFromBackend();

    return () => {
      cancelled = true;
    };
  }, [accessToken, toast]);

  async function setTheme(nextThemeKey: ThemeKey) {
    if (nextThemeKey === themeKey) {
      return;
    }

    const previous = themeKey;
    setThemeKey(nextThemeKey);

    if (!accessToken) {
      return;
    }

    setIsSaving(true);

    try {
      await updateCurrentUserPreferences({ themeKey: nextThemeKey, dateFormat }, accessToken);
      toast.success(
        "Theme updated",
        nextThemeKey === "dark" ? "Dark mode enabled." : nextThemeKey === "light" ? "Light mode enabled." : "System mode enabled."
      );
    } catch (error) {
      setThemeKey(previous);
      const message = (error as ApiError).message ?? "Could not persist theme preference.";
      toast.error("Theme update failed", message);
    } finally {
      setIsSaving(false);
    }
  }

  async function setDateFormat(nextDateFormat: DateFormat) {
    if (nextDateFormat === dateFormat) {
      return;
    }

    const previous = dateFormat;
    setDateFormatState(nextDateFormat);

    if (!accessToken) {
      return;
    }

    setIsSaving(true);

    try {
      await updateCurrentUserPreferences({ themeKey, dateFormat: nextDateFormat }, accessToken);
      toast.success("Date format updated", `Dates now display as ${nextDateFormat}.`);
    } catch (error) {
      setDateFormatState(previous);
      const message = (error as ApiError).message ?? "Could not persist date format preference.";
      toast.error("Date format update failed", message);
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <ThemeContext.Provider
      value={{ themeKey, resolvedThemeKey, dateFormat, isSaving, setTheme, setDateFormat }}
    >
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }

  return context;
}
