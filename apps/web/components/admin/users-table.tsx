"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { Pencil, Plus } from "lucide-react";
import { useAccessToken } from "@/components/auth/use-access-token";
import {
  AdminUser,
  CreateAdminUserPayload,
  UserStatus,
  createAdminUser,
  getAdminUsers,
  setAdminUserStatus,
  updateAdminUser,
} from "@/lib/api/admin-users";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { Select } from "@/components/ui/select";

const availableRoles = ["sysadmin", "admin"] as const;

interface UserFormState {
  email: string;
  displayName: string;
  password: string;
  status: UserStatus;
  roles: string[];
}

const emptyForm: UserFormState = {
  email: "",
  displayName: "",
  password: "",
  status: "Active",
  roles: ["admin"],
};

function toFormState(user: AdminUser): UserFormState {
  return {
    email: user.email,
    displayName: user.displayName ?? "",
    password: "",
    status: user.status,
    roles: user.roles,
  };
}

function roleBadgeVariant(role: string): "default" | "outline" {
  return role === "sysadmin" ? "default" : "outline";
}

export function UsersTable() {
  const accessToken = useAccessToken();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toggling, setToggling] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [form, setForm] = useState<UserFormState>(emptyForm);
  const [submitting, setSubmitting] = useState(false);

  const isEditing = editingUser !== null;
  const sortedUsers = useMemo(
    () => [...users].sort((a, b) => a.email.localeCompare(b.email)),
    [users]
  );

  const loadUsers = useCallback(async () => {
    if (!accessToken) {
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      setError(null);
      const data = await getAdminUsers(accessToken);
      setUsers(data);
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to load users.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }, [accessToken]);

  useEffect(() => {
    void loadUsers();
  }, [loadUsers]);

  function openCreate() {
    setEditingUser(null);
    setForm(emptyForm);
    setError(null);
    setDialogOpen(true);
  }

  function openEdit(user: AdminUser) {
    setEditingUser(user);
    setForm(toFormState(user));
    setError(null);
    setDialogOpen(true);
  }

  function toggleRole(role: string) {
    setForm((current) => ({
      ...current,
      roles: current.roles.includes(role)
        ? current.roles.filter((item) => item !== role)
        : [...current.roles, role],
    }));
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!accessToken || submitting) return;

    setSubmitting(true);
    setError(null);
    try {
      if (isEditing) {
        const updated = await updateAdminUser(
          editingUser.id,
          {
            email: form.email.trim(),
            displayName: form.displayName.trim() || null,
            status: form.status,
            roles: form.roles,
          },
          accessToken
        );
        setUsers((current) => current.map((user) => (user.id === updated.id ? updated : user)));
      } else {
        const payload: CreateAdminUserPayload = {
          email: form.email.trim(),
          displayName: form.displayName.trim() || null,
          password: form.password,
          status: form.status,
          roles: form.roles,
        };
        const created = await createAdminUser(payload, accessToken);
        setUsers((current) => [...current, created]);
      }

      setDialogOpen(false);
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to save user.";
      setError(message);
    } finally {
      setSubmitting(false);
    }
  }

  async function handleToggleStatus(user: AdminUser) {
    if (!accessToken || toggling) return;
    const next: UserStatus = user.status === "Active" ? "Suspended" : "Active";
    setToggling(user.id);
    setError(null);
    try {
      await setAdminUserStatus(user.id, next, accessToken);
      setUsers((prev) => prev.map((item) => (item.id === user.id ? { ...item, status: next } : item)));
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to update user status.";
      setError(message);
    } finally {
      setToggling(null);
    }
  }

  if (loading) {
    return <p className="text-sm text-muted-foreground">Loading users...</p>;
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <Button size="sm" onClick={openCreate}>
          <Plus className="h-4 w-4" />
          Add User
        </Button>
      </div>

      <div className="overflow-hidden rounded-md border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-4 py-3 text-left font-medium">Email</th>
              <th className="px-4 py-3 text-left font-medium">Name</th>
              <th className="px-4 py-3 text-left font-medium">Roles</th>
              <th className="px-4 py-3 text-left font-medium">Status</th>
              <th className="px-4 py-3 text-right font-medium">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {sortedUsers.map((user) => (
              <tr key={user.id} className="transition-colors hover:bg-muted/30">
                <td className="px-4 py-3 font-mono text-xs">{user.email}</td>
                <td className="px-4 py-3 text-muted-foreground">{user.displayName ?? "-"}</td>
                <td className="px-4 py-3">
                  <div className="flex flex-wrap gap-1">
                    {user.roles.length > 0 ? (
                      user.roles.map((role) => (
                        <Badge key={role} variant={roleBadgeVariant(role)} className="text-xs">
                          {role}
                        </Badge>
                      ))
                    ) : (
                      <span className="text-xs text-muted-foreground">No roles</span>
                    )}
                  </div>
                </td>
                <td className="px-4 py-3">
                  <Badge variant={user.status === "Active" ? "default" : "destructive"}>
                    {user.status}
                  </Badge>
                </td>
                <td className="px-4 py-3">
                  <div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" onClick={() => openEdit(user)}>
                      <Pencil className="h-3.5 w-3.5" />
                      Edit
                    </Button>
                    <Button
                      size="sm"
                      variant={user.status === "Active" ? "destructive" : "default"}
                      disabled={toggling === user.id}
                      onClick={() => void handleToggleStatus(user)}
                    >
                      {toggling === user.id ? "..." : user.status === "Active" ? "Suspend" : "Activate"}
                    </Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {sortedUsers.length === 0 ? (
          <p className="py-8 text-center text-sm text-muted-foreground">No users found.</p>
        ) : null}
      </div>

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>{isEditing ? "Edit User" : "Add User"}</DialogTitle>
            <DialogDescription>
              Manage platform user access, roles and account status.
            </DialogDescription>
          </DialogHeader>

          <form onSubmit={(event) => void handleSubmit(event)} className="space-y-4">
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="admin-user-email">
                Email
              </label>
              <Input
                id="admin-user-email"
                type="email"
                value={form.email}
                onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
                required
              />
            </div>

            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="admin-user-display-name">
                Display Name
              </label>
              <Input
                id="admin-user-display-name"
                value={form.displayName}
                onChange={(event) => setForm((current) => ({ ...current, displayName: event.target.value }))}
              />
            </div>

            {!isEditing ? (
              <div className="space-y-1">
                <label className="text-sm font-medium" htmlFor="admin-user-password">
                  Temporary Password
                </label>
                <PasswordInput
                  id="admin-user-password"
                  value={form.password}
                  onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
                  required
                  minLength={12}
                />
                <p className="text-xs text-muted-foreground">
                  Use at least 12 characters with uppercase, lowercase and a symbol.
                </p>
              </div>
            ) : null}

            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="admin-user-status">
                Status
              </label>
              <Select
                id="admin-user-status"
                value={form.status}
                onChange={(event) => setForm((current) => ({ ...current, status: event.target.value as UserStatus }))}
                className="w-full"
              >
                <option value="Active">Active</option>
                <option value="Suspended">Suspended</option>
              </Select>
            </div>

            <fieldset className="space-y-2">
              <legend className="text-sm font-medium">Roles</legend>
              <div className="flex flex-wrap gap-3">
                {availableRoles.map((role) => (
                  <label key={role} className="flex items-center gap-2 text-sm text-muted-foreground">
                    <input
                      type="checkbox"
                      checked={form.roles.includes(role)}
                      onChange={() => toggleRole(role)}
                      className="h-4 w-4 rounded border-border"
                    />
                    {role}
                  </label>
                ))}
              </div>
            </fieldset>

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="outline" onClick={() => setDialogOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? "Saving..." : isEditing ? "Save Changes" : "Create User"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
