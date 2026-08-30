import { redirect } from "next/navigation";
import { ContentGenerationJobsClient } from "@/components/platform/content-generation-jobs-client";
import { getServerAuthSession } from "@/lib/auth/server-session";

export default async function ContentGenerationJobsPage() {
  const session = await getServerAuthSession();
  if (!session.accessToken || session.isExpired) {
    redirect("/login");
  }

  if (!session.isPlatformAdmin) {
    redirect("/dashboard");
  }

  return <ContentGenerationJobsClient />;
}
