"use client";

import { useEffect, useState, useCallback } from "react";
import { useAccessToken } from "@/components/auth/use-access-token";
import {
  getAdminUsers,
  setAdminUserStatus,
  AdminUser,
  UserStatus,
} from "@/lib/api/admin-users";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";

export function UsersTable() {
  const { accessToken } = useAccessToken();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toggling, setToggling] = useState<string | null>(null);

  const loadUsers = useCallback(async () => {
    if (!accessToken) return;
    try {
      setLoading(true);
      setError(null);
      const data = await getAdminUsers(accessToken);
      setUsers(data);
    } catch {
      setError("Failed to load users.");
    } finally {
      setLoading(false);
    }
  }, [accessToken]);

  useEffect(() => {
    loadUsers();
  }, [loadUsers]);

  const handleToggleStatus = async (user: AdminUser) => {
    if (!accessToken || toggling) return;
    const next: UserStatus = user.status === "Active" ? "Suspended" : "Active";
    setToggling(user.id);
    try {
      await setAdminUserStatus(user.id, next, accessToken);
      setUsers((prev) =>
        prev.map((u) => (u.id === user.id ? { ...u, status: next } : u))
      );
    } catch {
      setError("Failed to update user status.");
    } finally {
      setToggling(null);
    }
  };

  if (loading) {
    return <p className="text-sm text-muted-foreground">Loading users...</p>;
  }

  if (error) {
    return <p className="text-sm text-destructive">{error}</p>;
  }

  return (
    <div className="rounded-md border overflow-hidden">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-4 py-3 text-left font-medium">Email</th>
            <th className="px-4 py-3 text-left font-medium">Name</th>
            <th className="px-4 py-3 text-left font-medium">Roles</th>
            <th className="px-4 py-3 text-left font-medium">Status</th>
            <th className="px-4 py-3 text-left font-medium">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {users.map((user) => (
            <tr
              key={user.id}
              className="hover:bg-muted/30 transition-colors"
            >
              <td className="px-4 py-3 font-mono text-xs">{user.email}</td>
              <td className="px-4 py-3 text-muted-foreground">
                {user.displayName ?? "—"}
              </td>
              <td className="px-4 py-3">
                <div className="flex gap-1 flex-wrap">
                  {user.roles.map((r) => (
                    <Badge key={r} variant="outline" className="text-xs">
                      {r}
                    </Badge>
                  ))}
                </div>
              </td>
              <td className="px-4 py-3">
                <Badge
                  variant={
                    user.status === "Active" ? "default" : "destructive"
                  }
                >
                  {user.status}
                </Badge>
              </td>
              <td className="px-4 py-3">
                <Button
                  size="sm"
                  variant={
                    user.status === "Active" ? "destructive" : "default"
                  }
                  disabled={toggling === user.id}
                  onClick={() => handleToggleStatus(user)}
                >
                  {toggling === user.id
                    ? "..."
                    : user.status === "Active"
                      ? "Suspend"
                      : "Activate"}
                </Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {users.length === 0 && (
        <p className="text-sm text-muted-foreground text-center py-8">
          No users found.
        </p>
      )}
    </div>
  );
}
