"use client";

// ContentTypeMixChart — PA-16 recharts pie/donut chart.
// Segments: Image / Video / Carousel percentages from stats.contentTypeMix.
// Graceful empty state when contentTypeMix is absent or empty (PA-18).

import {
  PieChart,
  Pie,
  Cell,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import { ProfileChartTooltip } from "@/components/social/profile-analyzer/dashboard/ProfileChartTooltip";
import type { ProfileStatsDto } from "@/lib/api/types/profile-analyzer";

// ── Constants ─────────────────────────────────────────────────────────────────

// Tailwind CSS-var colours for the segments (kept readable on dark/light).
const SEGMENT_COLORS: string[] = [
  "hsl(var(--primary))",
  "hsl(221 83% 53%)",   // blue-500 equivalent
  "hsl(142 71% 45%)",   // emerald-500 equivalent
  "hsl(38 92% 50%)",    // amber-500 equivalent
  "hsl(0 84% 60%)",     // red-500 equivalent
];

const CONTENT_TYPE_LABELS: Record<string, string> = {
  Image: "Image",
  Video: "Video",
  Carousel: "Carousel",
  Reel: "Reel",
  Story: "Story",
};

// ── Types ─────────────────────────────────────────────────────────────────────

interface ContentTypeMixChartProps {
  stats: ProfileStatsDto | null;
}

interface PieEntry {
  name: string;
  value: number;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function buildPieData(stats: ProfileStatsDto | null): PieEntry[] {
  if (!stats) return [];
  const entries = Object.entries(stats.contentTypeMix);
  if (entries.length === 0) return [];

  return entries
    .filter(([, pct]) => pct > 0)
    .map(([type, pct]) => ({
      name: CONTENT_TYPE_LABELS[type] ?? type,
      value: Math.round(pct * 10) / 10,
    }))
    .sort((a, b) => b.value - a.value);
}

function renderLabel({
  cx, cy, midAngle, innerRadius, outerRadius, value,
}: {
  cx: number;
  cy: number;
  midAngle: number;
  innerRadius: number;
  outerRadius: number;
  value: number;
}) {
  if (value < 8) return null; // skip tiny labels
  const RADIAN = Math.PI / 180;
  const radius = innerRadius + (outerRadius - innerRadius) * 0.6;
  const x = cx + radius * Math.cos(-midAngle * RADIAN);
  const y = cy + radius * Math.sin(-midAngle * RADIAN);
  return (
    <text
      x={x}
      y={y}
      fill="white"
      textAnchor="middle"
      dominantBaseline="central"
      fontSize={11}
      fontWeight={600}
    >
      {`${value}%`}
    </text>
  );
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ContentTypeMixChart({ stats }: ContentTypeMixChartProps) {
  const data = buildPieData(stats);

  return (
    <div className="space-y-2">
      <p className="text-xs font-medium text-foreground/80">Content mix</p>
      {data.length === 0 ? (
        <div className="flex items-center justify-center h-32 rounded-md border border-border/30 border-dashed">
          <p className="text-xs text-muted-foreground/60">No content type data</p>
        </div>
      ) : (
        <div className="h-48">
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie
                data={data}
                cx="50%"
                cy="50%"
                innerRadius={42}
                outerRadius={72}
                dataKey="value"
                labelLine={false}
                label={renderLabel}
              >
                {data.map((_, i) => (
                  <Cell
                    key={`cell-${i}`}
                    fill={SEGMENT_COLORS[i % SEGMENT_COLORS.length]}
                  />
                ))}
              </Pie>
              <Tooltip
                wrapperStyle={{ outline: "none", zIndex: 50 }}
                content={
                  <ProfileChartTooltip
                    valueFormatter={(value) => `${Number(value).toLocaleString("en-US")}%`}
                  />
                }
              />
              <Legend
                iconSize={10}
                wrapperStyle={{ fontSize: 11 }}
                formatter={(value) => (
                  <span style={{ color: "hsl(var(--foreground))", opacity: 0.75 }}>
                    {value}
                  </span>
                )}
              />
            </PieChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}
