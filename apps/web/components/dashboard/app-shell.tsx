"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useState } from "react";
import { motion } from "framer-motion";
import { cn } from "@/lib/utils";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { ThemeProvider } from "@/components/theme/theme-provider";
import { ThemeSelector } from "@/components/theme/theme-selector";
import { SidebarAreaSwitcher } from "@/components/dashboard/sidebar-area-switcher";
import { adminNavigation, getWorkspaceNavigation, workspaceModules } from "@/lib/navigation";
import type { NavGroup, WorkspaceModule, WorkspaceModuleKey } from "@/lib/navigation";
import { ThemeKey } from "@/lib/api/types";
import { HttpLoader } from "@/components/ui/http-loader";
import { ProgressBar } from "@/components/ui/progress-bar";
import { Button } from "@/components/ui/button";

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

function ModuleSwitcher({
  modules,
  activeModule,
  onChange
}: {
  modules: WorkspaceModule[];
  activeModule: WorkspaceModuleKey;
  onChange: (moduleKey: WorkspaceModuleKey) => void;
}) {
  return (
    <div className="rounded-lg border border-border bg-muted/40 p-1">
      <p className="mb-1 px-2 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/50">
        Module
      </p>
      <div className="grid gap-1">
        {modules.map((module) => {
          const Icon = module.icon;
          const active = activeModule === module.key;

          return (
            <Button
              key={module.key}
              type="button"
              variant="ghost"
              size="sm"
              aria-pressed={active}
              className={cn(
                "h-auto justify-start gap-2 px-2 py-2 text-left",
                active
                  ? "border border-primary/40 bg-primary/20 text-primary shadow-[0_0_18px_rgba(59,130,246,0.22)] hover:bg-primary/20"
                  : "text-muted-foreground hover:text-foreground"
              )}
              onClick={() => onChange(module.key)}
            >
              <Icon className="h-4 w-4 shrink-0" />
              <span className="min-w-0">
                <span className="block text-xs font-semibold leading-tight">{module.label}</span>
                <span className="block text-[10px] font-normal leading-tight text-muted-foreground">
                  {module.description}
                </span>
              </span>
            </Button>
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
  const router = useRouter();
  const [activeModuleKey, setActiveModuleKey] = useState<WorkspaceModuleKey>("social");
  const isAdminArea = pathname.startsWith("/admin");
  const activeArea = isAdminArea ? "admin" : "workspace";
  const activeModule = workspaceModules.find((module) => module.key === activeModuleKey) ?? workspaceModules[0];
  const navGroups = isAdminArea ? adminNavigation : getWorkspaceNavigation(activeModule.key);
  const layoutId = `sidebar-active-${activeArea}`;

  function changeModule(moduleKey: WorkspaceModuleKey) {
    setActiveModuleKey(moduleKey);
    if (!isAdminArea) {
      router.push("/dashboard");
    }
  }

  return (
    <ThemeProvider initialThemeKey={initialThemeKey}>
      <HttpLoader />
      <ProgressBar />
      <div className="min-h-screen bg-background">
        <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[220px_1fr]">
          <aside className="border-r border-border bg-card/50 p-3">
            <div className="mb-6">
              <p className="text-xs uppercase tracking-widest text-muted-foreground">Incoders Hive</p>
              <h1 className="text-xl font-semibold">{isAdminArea ? "Platform Admin" : activeModule.label}</h1>
            </div>

            {!isAdminArea ? (
              <div className="mb-5">
                <ModuleSwitcher
                  modules={workspaceModules}
                  activeModule={activeModule.key}
                  onChange={changeModule}
                />
              </div>
            ) : null}

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
                  <p className="text-sm text-muted-foreground">
                    {isAdminArea ? "Platform operations and governance" : activeModule.description}
                  </p>
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
