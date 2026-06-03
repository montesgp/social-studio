"use client";

// AvgViewsByWeekdayChart — PA-16, PA-12 recharts bar chart.
// X-axis: Mon–Sun (UTC weekday; 0=Mon, 6=Sun per backend convention).
// Y-axis: average views from avgViewsByWeekday weekday buckets.
// Graceful empty-state when data is absent or has no view data (PA-18).

import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from "recharts";
import type { ProfileStatsDto } from "@/lib/api/types/profile-analyzer";

// ── Constants ─────────────────────────────────────────────────────────────────

const WEEKDAY_LABELS: Record<string, string> = {
  "0": "Lun",
  "1": "Mar",
  "2": "Mié",
  "3": "Jue",
  "4": "Vie",
  "5": "Sáb",
  "6": "Dom",
};

// ── Types ─────────────────────────────────────────────────────────────────────

interface AvgViewsByWeekdayChartProps {
  stats: ProfileStatsDto | null;
}

interface ChartEntry {
  day: string;
  vistas: number;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function buildChartData(stats: ProfileStatsDto | null): ChartEntry[] {
  if (!stats) return [];
  const entries = Object.entries(stats.avgViewsByWeekday);
  if (entries.length === 0) return [];

  // Check if we have any non-null views data
  const hasViewData = entries.some(
    ([, metric]) => metric.avgViews !== null && metric.avgViews !== undefined
  );
  if (!hasViewData) return [];

  return entries
    .map(([key, metric]) => ({
      day: WEEKDAY_LABELS[key] ?? `D${key}`,
      vistas: Math.round(metric.avgViews ?? 0),
    }))
    .sort((a, b) => {
      // Sort by weekday key 0–6
      const keyA = Object.keys(WEEKDAY_LABELS).find(
        (k) => WEEKDAY_LABELS[k] === a.day
      );
      const keyB = Object.keys(WEEKDAY_LABELS).find(
        (k) => WEEKDAY_LABELS[k] === b.day
      );
      return parseInt(keyA ?? "0") - parseInt(keyB ?? "0");
    });
}

// ── Component ─────────────────────────────────────────────────────────────────

export function AvgViewsByWeekdayChart({ stats }: AvgViewsByWeekdayChartProps) {
  const data = buildChartData(stats);

  return (
    <div className="space-y-2">
      <p className="text-xs font-medium text-foreground/80">
        Vistas promedio por día de la semana
      </p>
      {data.length === 0 ? (
        <div className="flex items-center justify-center h-32 rounded-md border border-border/30 border-dashed">
          <p className="text-xs text-muted-foreground/60">
            Sin datos de vistas por día
          </p>
        </div>
      ) : (
        <div className="h-40">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data} margin={{ top: 4, right: 4, left: -16, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" className="stroke-border/40" />
              <XAxis
                dataKey="day"
                tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
                axisLine={false}
                tickLine={false}
              />
              <YAxis
                tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
                axisLine={false}
                tickLine={false}
                tickFormatter={(v: number) =>
                  v >= 1000 ? `${Math.round(v / 1000)}k` : String(v)
                }
              />
              <Tooltip
                contentStyle={{
                  fontSize: 12,
                  borderRadius: 8,
                  border: "1px solid hsl(var(--border))",
                  backgroundColor: "hsl(var(--popover))",
                  color: "hsl(var(--popover-foreground))",
                }}
                formatter={(value: number) => [
                  value.toLocaleString("es-AR"),
                  "Vistas promedio",
                ]}
              />
              <Bar
                dataKey="vistas"
                fill="hsl(var(--primary))"
                radius={[3, 3, 0, 0]}
                maxBarSize={40}
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}
