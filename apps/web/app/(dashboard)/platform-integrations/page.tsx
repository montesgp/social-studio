import { redirect } from "next/navigation";
import { PlatformIntegrationsClient } from "@/components/platform/platform-integrations-client";
import { getServerAuthSession } from "@/lib/auth/server-session";

export default async function PlatformIntegrationsPage() {
  const session = await getServerAuthSession();
  if (!session.accessToken || session.isExpired) {
    redirect("/login");
  }

  if (!session.isPlatformAdmin) {
    redirect("/dashboard");
  }

  return <PlatformIntegrationsClient />;
}
