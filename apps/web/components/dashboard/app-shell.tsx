"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { motion } from "framer-motion";
import { cn } from "@/lib/utils";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { useAccessToken } from "@/components/auth/use-access-token";
import { ThemeProvider } from "@/components/theme/theme-provider";
import { ThemeSelector } from "@/components/theme/theme-selector";
import { SidebarAreaSwitcher } from "@/components/dashboard/sidebar-area-switcher";
import { adminNavigation, getWorkspaceNavigation, workspaceModules } from "@/lib/navigation";
import type { NavGroup, WorkspaceModule, WorkspaceModuleKey } from "@/lib/navigation";
import { getWorkspaceModules } from "@/lib/api/me";
import { ThemeKey } from "@/lib/api/types";
import { ProgressBar } from "@/components/ui/progress-bar";
import { Button } from "@/components/ui/button";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { BlockingLoader } from "@/components/ui/blocking-loader";
import { subscribe as subscribeHttpLoading, getSnapshot as getHttpLoadingSnapshot } from "@/lib/loading/http-loading-store";
import {
  subscribe as subscribeBlockingLoader,
  getSnapshot as getBlockingLoaderSnapshot,
  getServerSnapshot as getBlockingLoaderServerSnapshot
} from "@/lib/loading/blocking-loader-store";
import { subscribe as subscribeSignOut, getSnapshot as getSignOutSnapshot } from "@/lib/auth/sign-out-store";

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
  const accessToken = useAccessToken();
  const { orgId, isInitialized: isOrgInitialized } = useActiveOrg();
  const [activeModuleKey, setActiveModuleKey] = useState<WorkspaceModuleKey>("hive-social");
  const [availableModules, setAvailableModules] = useState<WorkspaceModule[]>([]);
  const [modulesLoaded, setModulesLoaded] = useState(false);
  const [routeLoading, setRouteLoading] = useState(true);
  const [routeSawHttpPending, setRouteSawHttpPending] = useState(false);
  const routeLoadingTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const modulesLoadedRef = useRef(false);
  const isHttpLoading = useSyncExternalStore(subscribeHttpLoading, getHttpLoadingSnapshot, () => false);
  const blockingLoader = useSyncExternalStore(
    subscribeBlockingLoader,
    getBlockingLoaderSnapshot,
    getBlockingLoaderServerSnapshot
  );
  const isSigningOut = useSyncExternalStore(subscribeSignOut, getSignOutSnapshot, () => false);
  const isAdminArea = pathname.startsWith("/admin");
  const activeArea = isAdminArea ? "admin" : "workspace";
  const moduleOptions = useMemo(
    () => (isPlatformAdmin ? availableModules.length > 0 ? availableModules : workspaceModules : availableModules),
    [availableModules, isPlatformAdmin]
  );
  const activeModule = moduleOptions.find((module) => module.key === activeModuleKey) ?? moduleOptions[0] ?? null;
  const navGroups = isAdminArea ? adminNavigation : getWorkspaceNavigation(activeModule?.key);
  const layoutId = `sidebar-active-${activeArea}`;
  const waitingForInitialShellData =
    !accessToken || !modulesLoaded || (!isPlatformAdmin && !isOrgInitialized);

  useEffect(() => {
    modulesLoadedRef.current = modulesLoaded;
  }, [modulesLoaded]);

  useEffect(() => {
    if (!accessToken) {
      setAvailableModules([]);
      setModulesLoaded(false);
      return;
    }

    if (!isPlatformAdmin && !isOrgInitialized) {
      return;
    }

    let cancelled = false;
    // Token refreshes publish a new access token, which re-runs this effect.
    // Do NOT drop the shell back to the initial blocking loader after the first
    // module load; refresh permissions in the background instead.
    if (!modulesLoadedRef.current) {
      setModulesLoaded(false);
    }

    void (async () => {
      try {
        const apiModules = await getWorkspaceModules(accessToken);
        if (cancelled) return;

        const mapped = apiModules
          .map((module) => workspaceModules.find((candidate) => candidate.key === module.key))
          .filter((module): module is WorkspaceModule => module !== undefined);

        setAvailableModules(mapped);
        setActiveModuleKey((current) => {
          if (mapped.some((module) => module.key === current)) {
            return current;
          }

          return mapped[0]?.key ?? "hive-social";
        });
      } catch {
        if (cancelled) return;
        setAvailableModules(isPlatformAdmin ? workspaceModules : []);
      } finally {
        if (!cancelled) {
          setModulesLoaded(true);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [accessToken, isOrgInitialized, isPlatformAdmin, orgId]);

  useEffect(() => {
    if (routeLoadingTimeoutRef.current) {
      clearTimeout(routeLoadingTimeoutRef.current);
      routeLoadingTimeoutRef.current = null;
    }

    setRouteLoading(true);
    setRouteSawHttpPending(false);

    routeLoadingTimeoutRef.current = setTimeout(() => {
      setRouteLoading(false);
      routeLoadingTimeoutRef.current = null;
    }, 350);

    return () => {
      if (routeLoadingTimeoutRef.current) {
        clearTimeout(routeLoadingTimeoutRef.current);
        routeLoadingTimeoutRef.current = null;
      }
    };
  }, [pathname]);

  useEffect(() => {
    if (!routeLoading) return;
    if (isHttpLoading) {
      setRouteSawHttpPending(true);
      return;
    }

    if (routeSawHttpPending && !isHttpLoading) {
      setRouteLoading(false);
    }
  }, [routeLoading, routeSawHttpPending, isHttpLoading]);

  useEffect(() => {
    if (waitingForInitialShellData) {
      setRouteLoading(true);
    }
  }, [waitingForInitialShellData]);

  function changeModule(moduleKey: WorkspaceModuleKey) {
    setActiveModuleKey(moduleKey);
    if (!isAdminArea) {
      router.push("/dashboard");
    }
  }

  return (
    <ThemeProvider initialThemeKey={initialThemeKey}>
      <ProgressBar />
      <BlockingLoader
        visible={waitingForInitialShellData || routeLoading || blockingLoader.isVisible}
        title={
          isSigningOut
            ? "Cerrando sesión…"
            : waitingForInitialShellData
            ? "Preparing workspace"
            : blockingLoader.isVisible
            ? blockingLoader.title
            : "Loading tool"
        }
        description={
          isSigningOut
            ? undefined
            : waitingForInitialShellData
            ? "Loading modules, permissions and menu context..."
            : blockingLoader.isVisible
            ? blockingLoader.description
            : "Fetching data required to render this view..."
        }
        mode={
          isSigningOut
            ? "spinner"
            : waitingForInitialShellData
            ? "progress"
            : blockingLoader.isVisible
            ? blockingLoader.mode
            : "spinner"
        }
      />
      <div className="min-h-screen bg-background">
        <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[220px_1fr]">
          <aside className="border-r border-border bg-card/50 p-3">
            <div className="mb-6">
              <p className="text-xs uppercase tracking-widest text-muted-foreground">Incoders Hive</p>
              <h1 className="text-xl font-semibold">{isAdminArea ? "Platform Admin" : activeModule?.label ?? "Workspace"}</h1>
            </div>

            {!isAdminArea ? (
              <div className="mb-5">
                {moduleOptions.length > 0 ? (
                  <ModuleSwitcher
                    modules={moduleOptions}
                    activeModule={activeModule?.key ?? "hive-social"}
                    onChange={changeModule}
                  />
                ) : modulesLoaded ? (
                  <div className="rounded-lg border border-border bg-muted/40 p-3 text-xs text-muted-foreground">
                    No modules assigned for this organization.
                  </div>
                ) : (
                  <div className="rounded-lg border border-border bg-muted/40 p-3 text-xs text-muted-foreground">
                    Loading modules...
                  </div>
                )}
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
                    {isAdminArea
                      ? "Platform operations and governance"
                      : activeModule?.description ?? "No module assigned to this workspace context."}
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
