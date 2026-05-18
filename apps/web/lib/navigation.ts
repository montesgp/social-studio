import {
  LayoutDashboard,
  Clapperboard,
  Sparkles,
  Send,
  BriefcaseBusiness,
  Building2,
  Users,
  Share2,
  ScrollText,
  ShieldCheck,
  CalendarClock,
  PlugZap,
  ScanSearch
} from "lucide-react";
import type { LucideIcon } from "lucide-react";

export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  /** When true, item is displayed as non-interactive with a "Soon" badge. */
  comingSoon?: boolean;
}

export interface NavGroup {
  /** Optional muted section label rendered above the group items. */
  label?: string;
  items: NavItem[];
}

/**
 * Workspace navigation — daily operational workflow.
 * Order is intentional: primary workflow first, management last.
 *
 * Route rewrites (next.config.ts) map clean URLs to existing pages:
 *   /content-generator → /workflow
 *   /ai-ideas         → /ideas
 */
export const workspaceNavigation: NavGroup[] = [
  {
    items: [
      { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard }
    ]
  },
  {
    label: "Content",
    items: [
      { href: "/content-generator", label: "Generator", icon: Clapperboard },
      { href: "/single-analyzer", label: "Single Analyzer", icon: ScanSearch },
      { href: "/ai-ideas", label: "AI Ideas", icon: Sparkles },
      { href: "/publications", label: "Publications", icon: Send }
    ]
  },
  {
    label: "Management",
    items: [
      { href: "/clients", label: "Clients", icon: BriefcaseBusiness },
      { href: "/settings/integrations", label: "Settings / Integrations", icon: PlugZap }
    ]
  }
];

/**
 * Admin navigation — platform/sysadmin operations.
 * Only rendered for users with IsPlatformAdmin = true.
 *
 * Route rewrites (next.config.ts) map admin URLs to existing pages:
 *   /admin/organizations → /organizations
 *   /admin/users         → /members
 *
 * Items marked comingSoon will have dedicated pages once scaffolded.
 */
export const adminNavigation: NavGroup[] = [
  {
    items: [
      { href: "/admin/organizations", label: "Organizations", icon: Building2 },
      { href: "/admin/users", label: "Users / Members", icon: Users }
    ]
  },
  {
    label: "System",
    items: [
      { href: "/admin/social-networks", label: "Social Networks", icon: Share2 },
      { href: "/admin/app-logs", label: "App Logs", icon: ScrollText, comingSoon: true },
      { href: "/admin/audit-logs", label: "Audit Logs", icon: ShieldCheck, comingSoon: true },
      { href: "/admin/jobs", label: "Scheduled Jobs", icon: CalendarClock, comingSoon: true }
    ]
  }
];
