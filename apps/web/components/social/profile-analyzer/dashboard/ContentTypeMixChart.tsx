"use client";

// ContentTypeMixChart — PA-16 recharts pie/donut chart.
// Segments: Image / Video / Carousel percentages from stats.contentTypeMix.
// Graceful empty state when contentTypeMix is absent or empty (PA-18).

import { useEffect, useState } from "react";
import {
  PieChart,
  Pie,
  Cell,
  Legend,
  ResponsiveContainer,
} from "recharts";
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
  captureMode?: boolean;
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

export function ContentTypeMixChart({
  stats,
  captureMode = false,
}: ContentTypeMixChartProps) {
  const data = buildPieData(stats);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);

  useEffect(() => {
    if (captureMode) {
      setActiveIndex(null);
    }
  }, [captureMode]);

  return (
    <div className="space-y-2">
      <p className="text-xs font-medium text-foreground/80">Content mix</p>
      {data.length === 0 ? (
        <div className="flex items-center justify-center h-32 rounded-md border border-border/30 border-dashed">
          <p className="text-xs text-muted-foreground/60">No content type data</p>
        </div>
      ) : (
        <div
          className="relative h-48"
          onMouseLeave={captureMode ? undefined : () => setActiveIndex(null)}
        >
          {!captureMode && activeIndex !== null && data[activeIndex] && (
            <div className="pointer-events-none absolute right-0 top-0 z-50 min-w-[120px] rounded-md border border-border/60 bg-popover/95 px-3 py-2 shadow-md backdrop-blur-sm">
              <div className="flex items-center gap-2">
                <span
                  className="h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: SEGMENT_COLORS[activeIndex % SEGMENT_COLORS.length] }}
                />
                <span className="text-xs font-medium text-foreground">
                  {data[activeIndex].name}
                </span>
              </div>
              <p className="mt-0.5 pl-[18px] text-xs text-muted-foreground">
                {data[activeIndex].value}%
              </p>
            </div>
          )}
          <ResponsiveContainer width="100%" height="100%">
            <PieChart onMouseLeave={captureMode ? undefined : () => setActiveIndex(null)}>
              <Pie
                data={data}
                cx="50%"
                cy="50%"
                innerRadius={42}
                outerRadius={72}
                dataKey="value"
                isAnimationActive={!captureMode}
                labelLine={false}
                label={renderLabel}
                onMouseEnter={
                  captureMode ? undefined : (_, i) => setActiveIndex(i)
                }
              >
                {data.map((_, i) => (
                  <Cell
                    key={`cell-${i}`}
                    fill={SEGMENT_COLORS[i % SEGMENT_COLORS.length]}
                  />
                ))}
              </Pie>
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
