"use client";

// EngagementTrendChart — PA-16, PA-12 recharts area chart.
// Plots avg likes and avg comments by weekday (Mon–Sun, UTC weekday 0=Mon).
// Uses avgLikesByWeekday and avgCommentsByWeekday from server-derived stats.
// Graceful empty state when stats or weekday data are absent (PA-18).

import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import type { ProfileStatsDto } from "@/lib/api/types/profile-analyzer";

// ── Constants ─────────────────────────────────────────────────────────────────

const WEEKDAY_LABELS: Record<string, string> = {
  "0": "Mon",
  "1": "Tue",
  "2": "Wed",
  "3": "Thu",
  "4": "Fri",
  "5": "Sat",
  "6": "Sun",
};

// ── Types ─────────────────────────────────────────────────────────────────────

interface EngagementTrendChartProps {
  stats: ProfileStatsDto | null;
}

interface ChartEntry {
  day: string;
  weekdayKey: number;
  likes: number;
  comments: number;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function buildChartData(stats: ProfileStatsDto | null): ChartEntry[] {
  if (!stats) return [];

  // Merge likes and comments weekday buckets
  const allKeys = new Set([
    ...Object.keys(stats.avgLikesByWeekday),
    ...Object.keys(stats.avgCommentsByWeekday),
  ]);

  if (allKeys.size === 0) return [];

  return Array.from(allKeys)
    .map((key) => {
      const likesMetric = stats.avgLikesByWeekday[key];
      const commentsMetric = stats.avgCommentsByWeekday[key];
      return {
        day: WEEKDAY_LABELS[key] ?? `D${key}`,
        weekdayKey: parseInt(key, 10),
        likes: Math.round(likesMetric?.avgLikes ?? 0),
        comments: Math.round(commentsMetric?.avgComments ?? 0),
      };
    })
    .sort((a, b) => a.weekdayKey - b.weekdayKey);
}

// ── Component ─────────────────────────────────────────────────────────────────

export function EngagementTrendChart({ stats }: EngagementTrendChartProps) {
  const data = buildChartData(stats);

  return (
    <div className="space-y-2">
      <p className="text-xs font-medium text-foreground/80">
        Average engagement by day
      </p>
      {data.length === 0 ? (
        <div className="flex items-center justify-center h-32 rounded-md border border-border/30 border-dashed">
          <p className="text-xs text-muted-foreground/60">
            No engagement data by day
          </p>
        </div>
      ) : (
        <div className="h-40">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={data} margin={{ top: 4, right: 4, left: -16, bottom: 0 }}>
              <defs>
                <linearGradient id="gradLikes" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="hsl(var(--primary))" stopOpacity={0.3} />
                  <stop offset="95%" stopColor="hsl(var(--primary))" stopOpacity={0} />
                </linearGradient>
                <linearGradient id="gradComments" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="hsl(142 71% 45%)" stopOpacity={0.3} />
                  <stop offset="95%" stopColor="hsl(142 71% 45%)" stopOpacity={0} />
                </linearGradient>
              </defs>
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
              />
              <Legend
                iconSize={10}
                wrapperStyle={{ fontSize: 11 }}
                formatter={(value) => (
                  <span style={{ color: "hsl(var(--foreground))", opacity: 0.75 }}>
                    {value === "likes" ? "Likes" : "Comments"}
                  </span>
                )}
              />
              <Area
                type="monotone"
                dataKey="likes"
                stroke="hsl(var(--primary))"
                strokeWidth={2}
                fill="url(#gradLikes)"
                dot={false}
              />
              <Area
                type="monotone"
                dataKey="comments"
                stroke="hsl(142 71% 45%)"
                strokeWidth={2}
                fill="url(#gradComments)"
                dot={false}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}
