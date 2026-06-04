"use client";

// AnalyzerModeSwitch — top-level Content | Profile switch (PA-19).
//
// "Content" renders the existing <SingleAnalyzerClient> (Mundo 1) — ZERO changes to that file.
// "Profile" lazy-loads <ProfileAnalyzerModule> (Mundo 2) so recharts stays out of the Mundo 1 bundle.
//
// This wrapper is the ONLY file that references both components. single-analyzer-client.tsx
// is left completely untouched (PA-REG-01).

import { lazy, Suspense, useState } from "react";
import { Film, UserCircle, RefreshCw } from "lucide-react";
import { SingleAnalyzerClient } from "@/components/social/single-analyzer-client";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

// Lazy-load the profile module so recharts is never bundled into the Content path.
const ProfileAnalyzerModule = lazy(
  () =>
    import(
      "@/components/social/profile-analyzer/ProfileAnalyzerModule"
    ).then((m) => ({ default: m.ProfileAnalyzerModule }))
);

// ── Types ─────────────────────────────────────────────────────────────────────

type AnalyzerMode = "content" | "profile";

// ── Props ─────────────────────────────────────────────────────────────────────

interface AnalyzerModeSwitchProps {
  isPlatformAdmin: boolean;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function AnalyzerModeSwitch({ isPlatformAdmin }: AnalyzerModeSwitchProps) {
  const [mode, setMode] = useState<AnalyzerMode>("content");

  return (
    <div className="flex flex-col gap-4">
      {/* Mode tab bar */}
      <div className="flex px-6 pt-4 gap-0">
        <ModeTab
          active={mode === "content"}
          icon={<Film className="h-3.5 w-3.5" />}
          label="Content"
          onClick={() => setMode("content")}
        />
        <ModeTab
          active={mode === "profile"}
          icon={<UserCircle className="h-3.5 w-3.5" />}
          label="Profile"
          onClick={() => setMode("profile")}
        />
      </div>

      {/* Panel — Content renders synchronously; Profile is Suspense-guarded */}
      {mode === "content" ? (
        <SingleAnalyzerClient isPlatformAdmin={isPlatformAdmin} />
      ) : (
        <Suspense
          fallback={
            <div className="flex items-center justify-center min-h-[200px] gap-2 text-sm text-muted-foreground">
              <RefreshCw className="h-4 w-4 animate-spin" />
              Loading profile analyzer…
            </div>
          }
        >
          {/* ProfileAnalyzerModule is wrapped in a padding container that mirrors
              the SingleAnalyzerClient layout (p-6 max-w-screen-xl mx-auto). */}
          <div className="flex flex-col gap-6 p-6 max-w-[92rem] mx-auto">
            <ProfileAnalyzerModule />
          </div>
        </Suspense>
      )}
    </div>
  );
}

// ── ModeTab ───────────────────────────────────────────────────────────────────

interface ModeTabProps {
  active: boolean;
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
}

function ModeTab({ active, icon, label, onClick }: ModeTabProps) {
  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        "h-auto rounded-none border-b-2 px-4 py-2 text-sm font-medium",
        active
          ? "border-primary text-primary"
          : "border-transparent text-muted-foreground hover:text-foreground hover:border-border"
      )}
    >
      {icon}
      {label}
    </Button>
  );
}
