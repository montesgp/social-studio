"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { motion } from "framer-motion";
import { cn } from "@/lib/utils";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { ThemeProvider } from "@/components/theme/theme-provider";
import { ThemeSelector } from "@/components/theme/theme-selector";
import { SidebarAreaSwitcher } from "@/components/dashboard/sidebar-area-switcher";
import { workspaceNavigation, adminNavigation } from "@/lib/navigation";
import type { NavGroup } from "@/lib/navigation";
import { ThemeKey } from "@/lib/api/types";
import { HttpLoader } from "@/components/ui/http-loader";
import { ProgressBar } from "@/components/ui/progress-bar";

function NavGroupSection({
  group,
  pathname,
  layoutId
}: {
  group: NavGroup;
  pathname: string;
  layoutId: string;
}) {
  return (
    <div>
      {group.label ? (
        <p className="mb-1 mt-5 px-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/50">
          {group.label}
        </p>
      ) : null}
      <div className="space-y-0.5">
        {group.items.map((item) => {
          const Icon = item.icon;

          if (item.comingSoon) {
            return (
              <div
                key={item.href}
                className="flex cursor-default items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground/40"
              >
                <Icon className="h-4 w-4 shrink-0" />
                <span className="flex-1">{item.label}</span>
                <span className="rounded-full border border-border/50 px-1.5 py-px text-[9px] font-medium uppercase tracking-widest text-muted-foreground/40">
                  Soon
                </span>
              </div>
            );
          }

          const active = pathname === item.href;
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "relative flex items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground transition-colors hover:text-foreground",
                active && "text-primary"
              )}
            >
              {active ? (
                <motion.span
                  layoutId={layoutId}
                  className="absolute inset-0 rounded-md border border-primary/30 bg-primary/15"
                  transition={{ type: "spring", duration: 0.35 }}
                />
              ) : null}
              <Icon className="relative z-10 h-4 w-4 shrink-0" />
              <span className="relative z-10">{item.label}</span>
            </Link>
          );
        })}
      </div>
    </div>
  );
}

export function AppShell({
  children,
  userEmail,
  initialThemeKey,
  isPlatformAdmin
}: {
  children: React.ReactNode;
  userEmail?: string;
  initialThemeKey: ThemeKey;
  isPlatformAdmin: boolean;
}) {
  const pathname = usePathname();
  const isAdminArea = pathname.startsWith("/admin");
  const activeArea = isAdminArea ? "admin" : "workspace";
  const navGroups = isAdminArea ? adminNavigation : workspaceNavigation;
  const layoutId = `sidebar-active-${activeArea}`;

  return (
    <ThemeProvider initialThemeKey={initialThemeKey}>
      <HttpLoader />
      <ProgressBar />
      <div className="min-h-screen bg-background">
        <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[220px_1fr]">
          <aside className="border-r border-border bg-card/50 p-3">
            <div className="mb-6">
              <p className="text-xs uppercase tracking-widest text-muted-foreground">Incoders Hive</p>
              <h1 className="text-xl font-semibold">Hive Social</h1>
            </div>

            {isPlatformAdmin ? (
              <div className="mb-5">
                <SidebarAreaSwitcher activeArea={activeArea} />
              </div>
            ) : null}

            <nav>
              {navGroups.map((group, i) => (
                <NavGroupSection key={i} group={group} pathname={pathname} layoutId={layoutId} />
              ))}
            </nav>
          </aside>

          <div className="flex min-h-screen flex-col">
            <header className="border-b border-border px-3 py-3">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <p className="text-sm text-muted-foreground">Workflow-oriented AI-native SaaS module</p>
                </div>
                <div className="rounded-lg border border-border bg-card/50 px-3 py-2">
                  <p className="text-[11px] uppercase tracking-widest text-muted-foreground">User</p>
                  <div className="mt-1 flex items-center gap-2">
                    <span className="rounded-md border border-border px-3 py-1 text-xs text-muted-foreground">
                      {userEmail ?? "Authenticated user"}
                    </span>
                    <ThemeSelector />
                    <SignOutButton />
                  </div>
                </div>
              </div>
            </header>

            <main className="flex-1 p-3">
              <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.2 }}>
                {children}
              </motion.div>
            </main>
          </div>
        </div>
      </div>
    </ThemeProvider>
  );
}
