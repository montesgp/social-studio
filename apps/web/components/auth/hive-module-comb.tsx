import {
  Bot,
  Megaphone,
  Plug,
  Send,
  Share2,
  ShoppingCart,
  Sparkles,
  Users,
  Wallet,
  type LucideIcon
} from "lucide-react";

/**
 * The cluster of cells beside the sign-in card.
 *
 * Hive is one comb, not one product: content generation is simply the first cell that lit up. This
 * shows the whole shape of the platform on the very first screen, and marks honestly which cells are
 * live today and which are still empty.
 */

type ModuleStatus = "live" | "beta" | "soon";

interface HiveModule {
  label: string;
  icon: LucideIcon;
  status: ModuleStatus;
  /** Grid position in the comb, in cell coordinates. */
  column: number;
  row: number;
}

const CELL_WIDTH = 96;
const CELL_HEIGHT = 83;
const COLUMN_STEP = CELL_WIDTH * 0.75;

const MODULES: HiveModule[] = [
  { label: "Content", icon: Sparkles, status: "live", column: 1, row: 1 },
  { label: "Publications", icon: Send, status: "live", column: 1, row: 0 },
  { label: "Social", icon: Share2, status: "live", column: 0, row: 1 },
  { label: "CRM", icon: Users, status: "beta", column: 0, row: 2 },
  { label: "Marketing", icon: Megaphone, status: "soon", column: 1, row: 2 },
  { label: "APIs", icon: Plug, status: "soon", column: 2, row: 1 },
  { label: "Finance", icon: Wallet, status: "soon", column: 2, row: 2 },
  { label: "Commerce", icon: ShoppingCart, status: "soon", column: 3, row: 1 },
  { label: "Agents", icon: Bot, status: "soon", column: 3, row: 2 }
];

const STATUS_STYLES: Record<ModuleStatus, string> = {
  live: "bg-primary/25 text-foreground ring-1 ring-inset ring-primary/60",
  beta: "bg-primary/10 text-foreground/90 ring-1 ring-inset ring-primary/25",
  soon: "bg-foreground/[0.06] text-muted-foreground ring-1 ring-inset ring-border/70"
};

const COMB_WIDTH = 3 * COLUMN_STEP + CELL_WIDTH;
const COMB_HEIGHT = 2 * CELL_HEIGHT + CELL_HEIGHT / 2 + CELL_HEIGHT;

/** Flat-top hexagon. */
const HEX_CLIP = "polygon(25% 0%, 75% 0%, 100% 50%, 75% 100%, 25% 100%, 0% 50%)";

export function HiveModuleComb({ className }: { className?: string }) {
  return (
    <div className={className}>
      <div className="relative" style={{ width: COMB_WIDTH, height: COMB_HEIGHT }}>
        {MODULES.map((module) => {
          const Icon = module.icon;
          const left = module.column * COLUMN_STEP;
          const top = module.row * CELL_HEIGHT + (module.column % 2 === 0 ? 0 : CELL_HEIGHT / 2);

          return (
            <div
              key={module.label}
              className="absolute"
              style={{ left, top, width: CELL_WIDTH, height: CELL_HEIGHT }}
            >
              <div
                className={`flex h-full w-full flex-col items-center justify-center gap-1 backdrop-blur-sm ${STATUS_STYLES[module.status]}`}
                style={{ clipPath: HEX_CLIP }}
              >
                <Icon className="h-4 w-4" strokeWidth={1.75} aria-hidden="true" />
                <span className="px-2 text-center text-[10px] font-medium leading-tight">
                  {module.label}
                </span>
              </div>
            </div>
          );
        })}
      </div>

      <div className="mt-8 max-w-xs space-y-3">
        <p className="text-sm leading-relaxed text-muted-foreground">
          One comb for the whole company: content, clients, campaigns, finance, commerce and the
          agents that run them — multi-organization from the first cell.
        </p>
        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-muted-foreground">
          <li className="flex items-center gap-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-primary" aria-hidden="true" />
            Live
          </li>
          <li className="flex items-center gap-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-primary/40" aria-hidden="true" />
            Beta
          </li>
          <li className="flex items-center gap-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-muted-foreground/50" aria-hidden="true" />
            Coming
          </li>
        </ul>
      </div>
    </div>
  );
}
