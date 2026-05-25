import { getServerAuthSession } from "@/lib/auth/server-session";
import { ClientsClient } from "@/components/social/clients/clients-client";

export default async function ClientsPage() {
  const session = await getServerAuthSession();
  return <ClientsClient isPlatformAdmin={session.isPlatformAdmin} />;
}
