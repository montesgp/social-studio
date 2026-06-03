"use client";

// OverviewCards — PA-16 metric cards for the profile analysis dashboard.
// Renders followers, posts, engagement rate, median views, posting cadence,
// and owner post count. All values sourced from ProfileDashboardResponse.stats;
// no re-computation at render time. Graceful absent-data indicators (PA-18).

import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import type { ProfileDashboardResponse } from "@/lib/api/types/profile-analyzer";

// ── Types ─────────────────────────────────────────────────────────────────────

interface OverviewCardsProps {
  dashboard: ProfileDashboardResponse;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(value: number | null | undefined, decimals = 0): string {
  if (value === null || value === undefined) return "—";
  return value.toLocaleString("es-AR", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
}

function fmtPct(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—";
  return `${value.toLocaleString("es-AR", { minimumFractionDigits: 1, maximumFractionDigits: 2 })}%`;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function OverviewCards({ dashboard }: OverviewCardsProps) {
  const stats = dashboard.stats;

  const isPartial =
    dashboard.status === "ProfileScrapedPartial" ||
    (stats && stats.degradedPostCount > 0);

  const cards = [
    {
      label: "Publicaciones analizadas",
      value: stats ? fmt(stats.ownerPostCount) : "—",
      sub: stats && stats.degradedPostCount > 0
        ? `${stats.degradedPostCount} degradadas`
        : null,
    },
    {
      label: "Tasa de engagement",
      value: stats ? fmtPct(stats.engagementRate) : "—",
      sub: "por publicación (promedio)",
    },
    {
      label: "Vistas medianas",
      value: stats ? (stats.medianViews !== null ? fmt(stats.medianViews) : "N/A") : "—",
      sub: stats?.medianViews === null ? "sin datos de video" : "videos / reels",
    },
    {
      label: "Cadencia de publicación",
      value: stats ? `${fmt(stats.postingCadenceDays, 1)} días` : "—",
      sub: "entre publicaciones (promedio)",
    },
    {
      label: "Tipos de contenido",
      value: stats
        ? Object.keys(stats.contentTypeMix).length > 0
          ? Object.entries(stats.contentTypeMix)
              .sort(([, a], [, b]) => b - a)
              .slice(0, 2)
              .map(([type, pct]) => `${type} ${Math.round(pct)}%`)
              .join(" · ")
          : "—"
        : "—",
      sub: "mezcla principal",
    },
    {
      label: "Perfil analizado",
      value: dashboard.profileUrlOrHandle,
      sub: isPartial ? "análisis parcial" : null,
      mono: true,
    },
  ];

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium text-foreground/80">Resumen</h3>
        {isPartial && (
          <Badge variant="warning" className="text-xs">
            Datos parciales
          </Badge>
        )}
      </div>
      <div className="grid gap-3 grid-cols-2 sm:grid-cols-3">
        {cards.map((card) => (
          <Card key={card.label} className="border-border/40 bg-background/60">
            <CardContent className="p-4 space-y-1">
              <p className="text-xs text-muted-foreground/70 leading-tight">{card.label}</p>
              <p
                className={`text-lg font-semibold leading-tight truncate ${
                  card.mono ? "font-mono text-sm" : ""
                }`}
              >
                {card.value}
              </p>
              {card.sub && (
                <p className="text-xs text-muted-foreground/60 truncate">{card.sub}</p>
              )}
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
