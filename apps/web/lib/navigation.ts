import {
  LayoutDashboard,
  BrainCircuit,
  Clapperboard,
  Send,
  BriefcaseBusiness,
  Building2,
  Users,
  Share2,
  ScrollText,
  ShieldCheck,
  CalendarClock,
  Mail,
  Plug,
  PlugZap,
  ScanSearch,
  History,
  Store,
  ShoppingBag,
  Package,
  Tags,
  Settings
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

export type WorkspaceModuleKey = "hive-social" | "hive-ecommerce";

export interface WorkspaceModule {
  key: WorkspaceModuleKey;
  label: string;
  description: string;
  shortLabel: string;
  icon: LucideIcon;
}

export const workspaceModules: WorkspaceModule[] = [
  {
    key: "hive-social",
    label: "Hive Social",
    description: "Content operations",
    shortLabel: "Social",
    icon: Share2
  },
  {
    key: "hive-ecommerce",
    label: "Hive E-Commerce",
    description: "Commerce operations",
    shortLabel: "Commerce",
    icon: Store
  }
];

/**
 * Workspace navigation — daily operational workflow.
 * Order is intentional: primary workflow first, management last.
 *
 * Route rewrites (next.config.ts) map clean URLs to existing pages:
 *   /content-generator → /workflow
 *   /single-analyzer   → /analyzer
 */
const workspaceHomeNavigation: NavGroup[] = [
  {
    items: [
      { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard }
    ]
  }
];

const socialModuleNavigation: NavGroup[] = [
  {
    label: "Content",
    // Ordered to follow the operator's actual path through the module: analyse a reference first,
    // generate pieces from it, then decide in Publications what ships and where. AI Analysis sits
    // beside the analyzer because it answers the question that precedes both — which of the
    // candidates discovery already banked is worth spending generation budget on.
    items: [
      { href: "/single-analyzer", label: "Single Analyzer", icon: ScanSearch },
      { href: "/ai-analysis", label: "AI Analysis", icon: BrainCircuit },
      { href: "/content-generator", label: "Generator", icon: Clapperboard },
      { href: "/publications", label: "Publications", icon: Send }
    ]
  }
];

const ecommerceModuleNavigation: NavGroup[] = [
  {
    label: "Commerce",
    items: [
      { href: "/commerce/products", label: "Products", icon: Package, comingSoon: true },
      { href: "/commerce/orders", label: "Orders", icon: ShoppingBag, comingSoon: true },
      { href: "/commerce/catalog", label: "Catalog", icon: Tags, comingSoon: true },
      { href: "/commerce/storefront", label: "Storefront", icon: Store, comingSoon: true }
    ]
  }
];

const sharedManagementNavigation: NavGroup[] = [
  {
    label: "Management",
    items: [
      { href: "/clients", label: "Clients", icon: BriefcaseBusiness },
      { href: "/settings/integrations", label: "Settings / Integrations", icon: PlugZap },
      { href: "/settings/preferences", label: "Preferences", icon: Settings }
    ]
  }
];

export function getWorkspaceNavigation(moduleKey?: WorkspaceModuleKey): NavGroup[] {
  if (!moduleKey) {
    return [...workspaceHomeNavigation, ...sharedManagementNavigation];
  }

  return [
    ...workspaceHomeNavigation,
    ...(moduleKey === "hive-ecommerce"
      ? ecommerceModuleNavigation
      : moduleKey === "hive-social"
        ? socialModuleNavigation
        : []),
    ...sharedManagementNavigation
  ];
}

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
      { href: "/admin/niches", label: "Niches", icon: Tags },
      { href: "/admin/email-templates", label: "Email Templates", icon: Mail },
      { href: "/admin/platform-integrations", label: "Platform Integrations", icon: Plug },
      { href: "/admin/content-generation-jobs", label: "Content Generation Jobs", icon: History },
      { href: "/admin/app-logs", label: "App Logs", icon: ScrollText, comingSoon: true },
      { href: "/admin/audit-logs", label: "Audit Logs", icon: ShieldCheck, comingSoon: true },
      { href: "/admin/jobs", label: "Scheduled Jobs", icon: CalendarClock, comingSoon: true }
    ]
  }
];
