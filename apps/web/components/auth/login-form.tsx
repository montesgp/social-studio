"use client";

import { FormEvent, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { login } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/types";
import { saveAuthSession } from "@/lib/auth/client-session";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";

export function LoginForm() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const router = useRouter();

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    startTransition(async () => {
      try {
        const tokens = await login({ email, password });
        saveAuthSession(tokens);
      } catch (err) {
        setError((err as ApiError).message ?? "Invalid credentials.");
        return;
      }

      router.push("/dashboard");
      router.refresh();
    });
  }

  return (
    <form className="space-y-4" onSubmit={onSubmit}>
      <Input
        type="email"
        autoComplete="email"
        placeholder="you@company.com"
        value={email}
        onChange={(e) => setEmail(e.target.value)}
        required
      />
      <div className="space-y-2">
        <Input
          type="password"
          autoComplete="current-password"
          placeholder="Password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <div className="text-right">
          <Link href="/forgot-password" className="text-xs text-muted-foreground hover:text-primary">
            Forgot password?
          </Link>
        </div>
      </div>
      {error ? <p className="text-sm text-red-300">{error}</p> : null}
      <Button type="submit" className="w-full" disabled={pending}>
        {pending ? "Signing in..." : "Sign in"}
      </Button>
    </form>
  );
}
