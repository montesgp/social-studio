import { getServerAuthSession } from "@/lib/auth/server-session";
import { IntegrationsSettingsClient } from "@/components/integrations/integrations-settings-client";

export default async function SettingsIntegrationsPage() {
  const session = await getServerAuthSession();
  return <IntegrationsSettingsClient isPlatformAdmin={session.isPlatformAdmin} />;
}
