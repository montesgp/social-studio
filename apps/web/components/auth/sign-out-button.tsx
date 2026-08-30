"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";
import { clearAuthSession } from "@/lib/auth/client-session";
import { beginSignOut } from "@/lib/auth/sign-out-store";

export function SignOutButton() {
  const [pending, setPending] = useState(false);
  const router = useRouter();

  function onSignOut() {
    if (pending) return;
    setPending(true);
    beginSignOut();
    clearAuthSession();
    router.replace("/login");
  }

  return (
    <Button variant="outline" size="sm" onClick={onSignOut} disabled={pending}>
      <LogOut className="h-4 w-4" />
      {pending ? "Signing out..." : "Sign out"}
    </Button>
  );
}
