"use client";

import { Moon, Sun } from "lucide-react";
import { useTheme } from "@/components/theme/theme-provider";
import { cn } from "@/lib/utils";

export function ThemeSelector() {
  const { resolvedThemeKey, setTheme, isSaving } = useTheme();
  const isDark = resolvedThemeKey === "dark";

  return (
    <div className="inline-flex items-center gap-1 rounded-full border border-border bg-background p-1">
      <button
        type="button"
        onClick={() => {
          void setTheme("light");
        }}
        aria-label="Light mode"
        aria-pressed={!isDark}
        disabled={isSaving}
        className={cn(
          "flex h-8 w-8 items-center justify-center rounded-full text-muted-foreground transition-colors",
          !isDark && "bg-primary text-primary-foreground"
        )}
      >
        <Sun className="h-4 w-4" />
      </button>
      <button
        type="button"
        onClick={() => {
          void setTheme("dark");
        }}
        aria-label="Dark mode"
        aria-pressed={isDark}
        disabled={isSaving}
        className={cn(
          "flex h-8 w-8 items-center justify-center rounded-full text-muted-foreground transition-colors",
          isDark && "bg-primary text-primary-foreground"
        )}
      >
        <Moon className="h-4 w-4" />
      </button>
    </div>
  );
}
