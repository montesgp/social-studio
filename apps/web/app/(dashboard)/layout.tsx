import { AppShell } from "@/components/dashboard/app-shell";
import { IdleSessionProvider } from "@/components/auth/idle-session-provider";
import { ActiveOrgProvider } from "@/lib/auth/active-org-context";
import { redirect } from "next/navigation";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { getCurrentUser } from "@/lib/api/me";
import { getCurrentUserPreferences } from "@/lib/api/user-preferences";
import { DateFormat, ThemeKey } from "@/lib/api/types";

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
  let initialDateFormat: DateFormat = "dd/MM/yyyy";
  try {
    const preferences = await getCurrentUserPreferences(authSession.accessToken);
    initialThemeKey = preferences.themeKey;
    initialDateFormat = preferences.dateFormat;
  } catch {
    initialThemeKey = "dark";
    initialDateFormat = "dd/MM/yyyy";
  }

  return (
    <IdleSessionProvider>
      <ActiveOrgProvider>
        <AppShell
          userEmail={currentUser.email ?? undefined}
          initialThemeKey={initialThemeKey}
          initialDateFormat={initialDateFormat}
          isPlatformAdmin={currentUser.isPlatformAdmin}
        >
          {children}
        </AppShell>
      </ActiveOrgProvider>
    </IdleSessionProvider>
  );
}
