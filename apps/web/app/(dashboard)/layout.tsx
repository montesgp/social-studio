import { AppShell } from "@/components/dashboard/app-shell";
import { IdleSessionProvider } from "@/components/auth/idle-session-provider";
import { OrgContextInitializer } from "@/components/auth/org-context-initializer";
import { redirect } from "next/navigation";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { getCurrentUser } from "@/lib/api/me";
import { getCurrentUserPreferences } from "@/lib/api/user-preferences";
import { ThemeKey } from "@/lib/api/types";

export default async function DashboardLayout({ children }: { children: React.ReactNode }) {
  const authSession = await getServerAuthSession();

  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }

  let currentUser;
  try {
    currentUser = await getCurrentUser(authSession.accessToken);
  } catch {
    redirect("/login");
  }

  let initialThemeKey: ThemeKey = "dark";
  try {
    const preferences = await getCurrentUserPreferences(authSession.accessToken);
    initialThemeKey = preferences.themeKey;
  } catch {
    initialThemeKey = "dark";
  }

  return (
    <IdleSessionProvider>
      <OrgContextInitializer />
      <AppShell
        userEmail={currentUser.email ?? undefined}
        initialThemeKey={initialThemeKey}
        isPlatformAdmin={currentUser.isPlatformAdmin}
      >
        {children}
      </AppShell>
    </IdleSessionProvider>
  );
}
