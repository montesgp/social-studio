"use client";

// AnalyzerModeSwitch — top-level Contenido | Perfil switch (PA-19).
//
// "Contenido" renders the existing <SingleAnalyzerClient> (Mundo 1) — ZERO changes to that file.
// "Perfil"    lazy-loads <ProfileAnalyzerModule> (Mundo 2) so recharts stays out of the Mundo 1 bundle.
//
// This wrapper is the ONLY file that references both components. single-analyzer-client.tsx
// is left completely untouched (PA-REG-01).

import { lazy, Suspense, useState } from "react";
import { Film, UserCircle, RefreshCw } from "lucide-react";
import { SingleAnalyzerClient } from "@/components/social/single-analyzer-client";
import { cn } from "@/lib/utils";

// Lazy-load the profile module so recharts is never bundled into the Contenido path.
const ProfileAnalyzerModule = lazy(
  () =>
    import(
      "@/components/social/profile-analyzer/ProfileAnalyzerModule"
    ).then((m) => ({ default: m.ProfileAnalyzerModule }))
);

// ── Types ─────────────────────────────────────────────────────────────────────

type AnalyzerMode = "contenido" | "perfil";

// ── Props ─────────────────────────────────────────────────────────────────────

interface AnalyzerModeSwitchProps {
  isPlatformAdmin: boolean;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function AnalyzerModeSwitch({ isPlatformAdmin }: AnalyzerModeSwitchProps) {
  const [mode, setMode] = useState<AnalyzerMode>("contenido");

  return (
    <div className="flex flex-col gap-4">
      {/* Mode tab bar */}
      <div className="flex px-6 pt-4 gap-0">
        <ModeTab
          active={mode === "contenido"}
          icon={<Film className="h-3.5 w-3.5" />}
          label="Contenido"
          onClick={() => setMode("contenido")}
        />
        <ModeTab
          active={mode === "perfil"}
          icon={<UserCircle className="h-3.5 w-3.5" />}
          label="Perfil"
          onClick={() => setMode("perfil")}
        />
      </div>

      {/* Panel — Contenido renders synchronously; Perfil is Suspense-guarded */}
      {mode === "contenido" ? (
        <SingleAnalyzerClient isPlatformAdmin={isPlatformAdmin} />
      ) : (
        <Suspense
          fallback={
            <div className="flex items-center justify-center min-h-[200px] gap-2 text-sm text-muted-foreground">
              <RefreshCw className="h-4 w-4 animate-spin" />
              Cargando análisis de perfil…
            </div>
          }
        >
          {/* ProfileAnalyzerModule is wrapped in a padding container that mirrors
              the SingleAnalyzerClient layout (p-6 max-w-screen-xl mx-auto). */}
          <div className="flex flex-col gap-6 p-6 max-w-screen-xl mx-auto">
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
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex items-center gap-1.5 px-4 py-2 text-sm font-medium border-b-2 transition-colors",
        active
          ? "border-purple-500 text-purple-400"
          : "border-transparent text-muted-foreground hover:text-foreground hover:border-border"
      )}
    >
      {icon}
      {label}
    </button>
  );
}
