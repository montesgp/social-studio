"use client";

import { useRouter } from "next/navigation";
import { cn } from "@/lib/utils";

interface SidebarAreaSwitcherProps {
  activeArea: "workspace" | "admin";
}

export function SidebarAreaSwitcher({ activeArea }: SidebarAreaSwitcherProps) {
  const router = useRouter();

  return (
    <div
      role="tablist"
      aria-label="Navigation area"
      className="flex rounded-lg border border-border bg-muted/40 p-0.5"
    >
      <button
        role="tab"
        type="button"
        aria-selected={activeArea === "workspace"}
        className={cn(
          "flex-1 rounded-md px-3 py-1.5 text-xs font-medium transition-all duration-150",
          activeArea === "workspace"
            ? "bg-card text-foreground shadow-sm"
            : "text-muted-foreground hover:text-foreground"
        )}
        onClick={() => router.push("/dashboard")}
      >
        Workspace
      </button>
      <button
        role="tab"
        type="button"
        aria-selected={activeArea === "admin"}
        className={cn(
          "flex-1 rounded-md px-3 py-1.5 text-xs font-medium transition-all duration-150",
          activeArea === "admin"
            ? "bg-card text-foreground shadow-sm"
            : "text-muted-foreground hover:text-foreground"
        )}
        onClick={() => router.push("/admin/organizations")}
      >
        Admin
      </button>
    </div>
  );
}
