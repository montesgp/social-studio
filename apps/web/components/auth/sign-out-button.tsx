"use client";

import { useTransition } from "react";
import { useRouter } from "next/navigation";
import { LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";
import { clearAuthSession } from "@/lib/auth/client-session";

export function SignOutButton() {
  const [pending, startTransition] = useTransition();
  const router = useRouter();

  function onSignOut() {
    startTransition(async () => {
      clearAuthSession();
      router.push("/login");
      router.refresh();
    });
  }

  return (
    <Button variant="outline" size="sm" onClick={onSignOut} disabled={pending}>
      <LogOut className="h-4 w-4" />
      {pending ? "Signing out..." : "Sign out"}
    </Button>
  );
}
