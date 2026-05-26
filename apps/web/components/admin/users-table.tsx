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
  updateAdminUser
} from "@/lib/api/admin-users";
import { getOrganizationModules } from "@/lib/api/organization-modules";
import { listOrganizations } from "@/lib/api/organizations";
import { Organization } from "@/lib/api/types";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
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
import { Column, SortableChildTable } from "@/components/ui/sortable-child-table";

const availableRoles = ["sysadmin", "admin"] as const;

interface ModuleOption {
  key: string;
  label: string;
  description: string;
  shortLabel: string;
}

interface OrganizationModulesFormState {
  organizationId: string;
  orderedModuleKeys: string[];
}

interface UserFormState {
  email: string;
  displayName: string;
  password: string;
  status: UserStatus;
  roles: string[];
  organizationModules: OrganizationModulesFormState[];
}

const emptyForm: UserFormState = {
  email: "",
  displayName: "",
  password: "",
  status: "Active",
  roles: ["admin"],
  organizationModules: [],
};

const moduleColumns: Column<ModuleOption>[] = [
  {
    key: "module",
    label: "Module",
    render: (module) => <span className="font-medium">{module.label}</span>,
  },
  {
    key: "description",
    label: "Description",
    render: (module) => <span className="text-xs text-muted-foreground">{module.description}</span>,
  },
];

function normalizeModuleKeys(keys: readonly string[]): string[] {
  const normalized: string[] = [];
  const seen = new Set<string>();
  for (const rawKey of keys) {
    if (!rawKey || !rawKey.trim()) {
      continue;
    }

    const key = rawKey.trim().toLowerCase();
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    normalized.push(key);
  }

  return normalized;
}

function toFormState(user: AdminUser): UserFormState {
  const organizationModules = user.organizationModules.slice(0, 1);

  return {
    email: user.email,
    displayName: user.displayName ?? "",
    password: "",
    status: user.status,
    roles: user.roles,
    organizationModules: organizationModules.map((entry) => ({
      organizationId: entry.organizationId,
      orderedModuleKeys: normalizeModuleKeys(
        [...entry.modules]
          .sort((left, right) => left.sortOrder - right.sortOrder)
          .map((module) => module.key)
      ),
    })),
  };
}

function roleBadgeVariant(role: string): BadgeVariant {
  return role === "sysadmin" ? "info" : "warning";
}

function statusBadgeVariant(status: UserStatus): BadgeVariant {
  return status === "Active" ? "success" : "destructive";
}

function UserOrganizationModulesCell({
  user,
  organizationId,
}: {
  user: AdminUser;
  organizationId: string;
}) {
  const organizationModules = organizationId
    ? user.organizationModules.filter((entry) => entry.organizationId === organizationId)
    : user.organizationModules;

  if (organizationModules.length === 0 && user.roles.includes("sysadmin") && !organizationId) {
    return <span className="font-medium text-sky-200">Platform access</span>;
  }

  if (organizationModules.length === 0) {
    return <span>No module assignments</span>;
  }

  return (
    <div className="space-y-2">
      {organizationModules.map((entry) => (
        <div key={entry.organizationId} className="space-y-1">
          <p className="font-medium text-foreground">{entry.organizationName}</p>
          <div className="flex flex-wrap gap-1">
            {entry.modules.length > 0 ? (
              entry.modules
                .slice()
                .sort((left, right) => left.sortOrder - right.sortOrder)
                .map((module) => (
                  <Badge key={`${entry.organizationId}-${module.key}`} variant="outline" className="text-xs">
                    {module.shortLabel || module.label}
                  </Badge>
                ))
            ) : (
              <span>No modules</span>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

export function UsersTable() {
  const accessToken = useAccessToken();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [modulesByOrganization, setModulesByOrganization] = useState<Record<string, ModuleOption[]>>({});
  const [loadingModulesOrganizationId, setLoadingModulesOrganizationId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toggling, setToggling] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [form, setForm] = useState<UserFormState>(emptyForm);
  const [filterOrganizationId, setFilterOrganizationId] = useState("");
  const [selectedOrganizationId, setSelectedOrganizationId] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const isEditing = editingUser !== null;
  const formIsPlatformAdmin = form.roles.includes("sysadmin");
  const sortedUsers = useMemo(() => [...users].sort((a, b) => a.email.localeCompare(b.email)), [users]);
  const visibleUsers = useMemo(
    () =>
      filterOrganizationId
        ? sortedUsers.filter((user) =>
            user.organizationModules.some((entry) => entry.organizationId === filterOrganizationId)
          )
        : sortedUsers,
    [filterOrganizationId, sortedUsers]
  );

  const selectedOrganizationModules = useMemo(
    () => form.organizationModules.find((entry) => entry.organizationId === selectedOrganizationId) ?? null,
    [form.organizationModules, selectedOrganizationId]
  );

  const availableModulesForSelectedOrganization = useMemo(
    () => modulesByOrganization[selectedOrganizationId] ?? [],
    [modulesByOrganization, selectedOrganizationId]
  );

  const selectedOrganizationModuleItems = useMemo(() => {
    const byKey = new Map(availableModulesForSelectedOrganization.map((module) => [module.key, module]));
    return (selectedOrganizationModules?.orderedModuleKeys ?? [])
      .map((key) => byKey.get(key))
      .filter((module): module is ModuleOption => module !== undefined);
  }, [availableModulesForSelectedOrganization, selectedOrganizationModules]);

  const organizationNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const organization of organizations) {
      map.set(organization.id, organization.name);
    }
    return map;
  }, [organizations]);

  const ensureOrganizationModulesLoaded = useCallback(
    async (organizationId: string) => {
      if (!accessToken || !organizationId || modulesByOrganization[organizationId]) {
        return;
      }

      setLoadingModulesOrganizationId(organizationId);
      try {
        const response = await getOrganizationModules(organizationId, accessToken);
        const assignedModules = [...response.assignedModules]
          .sort((left, right) => left.sortOrder - right.sortOrder)
          .map((module) => ({
            key: module.moduleKey,
            label: module.displayName,
            description: module.description,
            shortLabel: module.shortLabel,
          }));

        setModulesByOrganization((current) => ({
          ...current,
          [organizationId]: assignedModules,
        }));

        setForm((current) => ({
          ...current,
          organizationModules: current.organizationModules.map((entry) =>
            entry.organizationId !== organizationId
              ? entry
              : {
                  organizationId: entry.organizationId,
                  orderedModuleKeys: normalizeModuleKeys(entry.orderedModuleKeys).filter((key) =>
                    assignedModules.some((module) => module.key === key)
                  ),
                }
          ),
        }));
      } catch {
        setModulesByOrganization((current) => ({
          ...current,
          [organizationId]: [],
        }));
      } finally {
        setLoadingModulesOrganizationId((current) => (current === organizationId ? null : current));
      }
    },
    [accessToken, modulesByOrganization]
  );

  const loadUsers = useCallback(async () => {
    if (!accessToken) {
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      setError(null);
      const [usersData, organizationsPage] = await Promise.all([
        getAdminUsers(accessToken),
        listOrganizations({ includeInactive: true, page: 1, pageSize: 100 }, accessToken),
      ]);

      const organizationItems = [...organizationsPage.items].sort((a, b) => a.name.localeCompare(b.name));
      setUsers(usersData);
      setOrganizations(organizationItems);
      setModulesByOrganization({});
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

  useEffect(() => {
    if (!dialogOpen || !selectedOrganizationId) {
      return;
    }

    void ensureOrganizationModulesLoaded(selectedOrganizationId);
  }, [dialogOpen, ensureOrganizationModulesLoaded, selectedOrganizationId]);

  function openCreate() {
    setEditingUser(null);
    setForm(emptyForm);
    setSelectedOrganizationId(organizations[0]?.id ?? "");
    setError(null);
    setDialogOpen(true);
  }

  function openEdit(user: AdminUser) {
    const nextForm = toFormState(user);
    setEditingUser(user);
    setForm(nextForm);
    setSelectedOrganizationId(nextForm.roles.includes("sysadmin") ? "" : nextForm.organizationModules[0]?.organizationId ?? organizations[0]?.id ?? "");
    setError(null);
    setDialogOpen(true);
  }

  function toggleRole(role: string) {
    setForm((current) => {
      const nextRoles = current.roles.includes(role)
        ? current.roles.filter((item) => item !== role)
        : [...current.roles, role];

      if (nextRoles.includes("sysadmin")) {
        setSelectedOrganizationId("");
      }

      return {
        ...current,
        roles: nextRoles,
        organizationModules: nextRoles.includes("sysadmin") ? [] : current.organizationModules,
      };
    });
  }

  function updateOrganizationModules(organizationId: string, orderedModuleKeys: string[]) {
    setForm((current) => {
      const availableKeys = new Set((modulesByOrganization[organizationId] ?? []).map((module) => module.key));
      const nextModules = normalizeModuleKeys(orderedModuleKeys).filter((key) => availableKeys.has(key));

      return {
        ...current,
        organizationModules:
          nextModules.length === 0
            ? []
            : [
                {
                  organizationId,
                  orderedModuleKeys: nextModules,
                },
              ],
      };
    });
  }

  function selectOrganization(organizationId: string) {
    setSelectedOrganizationId(organizationId);
    setForm((current) => {
      const currentSelection = current.organizationModules.find((entry) => entry.organizationId === organizationId);

      return {
        ...current,
        organizationModules: currentSelection ? [currentSelection] : [],
      };
    });
  }

  function toggleOrganizationModule(moduleKey: string) {
    if (!selectedOrganizationId) return;

    const current = selectedOrganizationModules?.orderedModuleKeys ?? [];
    const next = current.includes(moduleKey)
      ? current.filter((key) => key !== moduleKey)
      : [...current, moduleKey];

    updateOrganizationModules(selectedOrganizationId, next);
  }

  function removeOrganizationMapping() {
    if (!selectedOrganizationId) return;
    updateOrganizationModules(selectedOrganizationId, []);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!accessToken || submitting) return;

    setSubmitting(true);
    setError(null);
    try {
      const organizationModules = formIsPlatformAdmin
        ? []
        : form.organizationModules.map((entry) => ({
            organizationId: entry.organizationId,
            orderedModuleKeys: normalizeModuleKeys(entry.orderedModuleKeys),
          })).slice(0, 1);

      if (isEditing) {
        const updated = await updateAdminUser(
          editingUser.id,
          {
            email: form.email.trim(),
            displayName: form.displayName.trim() || null,
            status: form.status,
            roles: form.roles,
            organizationModules,
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
          organizationModules,
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
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Select
            className="min-w-[220px]"
            value={filterOrganizationId}
            onChange={(event) => setFilterOrganizationId(event.target.value)}
            disabled={organizations.length === 0}
          >
            <option value="">All organizations</option>
            {organizations.map((organization) => (
              <option key={organization.id} value={organization.id}>
                {organization.name}
              </option>
            ))}
          </Select>
          <Button size="sm" onClick={openCreate}>
            <Plus className="h-4 w-4" />
            Add User
          </Button>
        </div>
      </div>

      <div className="overflow-hidden rounded-md border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-4 py-3 text-left font-medium">Email</th>
              <th className="px-4 py-3 text-left font-medium">Name</th>
              <th className="px-4 py-3 text-left font-medium">Roles</th>
              <th className="px-4 py-3 text-left font-medium">Org / Modules</th>
              <th className="px-4 py-3 text-left font-medium">Status</th>
              <th className="px-4 py-3 text-right font-medium">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {visibleUsers.map((user) => (
              <tr key={user.id} className="transition-colors hover:bg-muted/30">
                <td className="px-4 py-3 font-mono text-xs">{user.email}</td>
                <td className="px-4 py-3 text-muted-foreground">{user.displayName ?? "-"}</td>
                <td className="px-4 py-3">
                  <div className="flex flex-wrap gap-1">
                    {user.roles.length > 0 ? (
                      user.roles.map((role) => (
                        <Badge key={role} variant={roleBadgeVariant(role)}>
                          {role}
                        </Badge>
                      ))
                    ) : (
                      <span className="text-xs text-muted-foreground">No roles</span>
                    )}
                  </div>
                </td>
                <td className="px-4 py-3 text-xs text-muted-foreground">
                  <UserOrganizationModulesCell user={user} organizationId={filterOrganizationId} />
                </td>
                <td className="px-4 py-3">
                  <Badge variant={statusBadgeVariant(user.status)}>
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
        {visibleUsers.length === 0 ? (
          <p className="py-8 text-center text-sm text-muted-foreground">No users found.</p>
        ) : null}
      </div>

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="max-h-[90vh] max-w-3xl overflow-y-auto">
          <DialogHeader>
            <DialogTitle>{isEditing ? "Edit User" : "Add User"}</DialogTitle>
            <DialogDescription>
              Manage platform user access, roles and module visibility per organization.
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

            <fieldset className="space-y-3 rounded-md border border-border p-3">
              <legend className="px-1 text-sm font-medium">Organization → Modules</legend>
              <div className="space-y-1">
                <label className="text-sm font-medium" htmlFor="admin-user-modules-organization">
                  Organization
                </label>
                <Select
                  id="admin-user-modules-organization"
                  className="w-full"
                  value={selectedOrganizationId}
                  onChange={(event) => selectOrganization(event.target.value)}
                  disabled={organizations.length === 0 || formIsPlatformAdmin}
                >
                  {organizations.length === 0 ? <option value="">No organizations available</option> : null}
                  {organizations.map((organization) => (
                    <option key={organization.id} value={organization.id}>
                      {organization.name}
                    </option>
                  ))}
                </Select>
                <p className="text-xs text-muted-foreground">
                  Platform sysadmins have platform-wide access without an organization assignment.
                </p>
              </div>

              {formIsPlatformAdmin ? (
                <p className="text-xs text-muted-foreground">No organization assignment applies to this role.</p>
              ) : selectedOrganizationId ? (
                <>
                  {loadingModulesOrganizationId === selectedOrganizationId ? (
                    <p className="text-xs text-muted-foreground">Loading organization modules...</p>
                  ) : null}

                  <div className="flex flex-wrap gap-3">
                    {availableModulesForSelectedOrganization.length > 0 ? (
                      availableModulesForSelectedOrganization.map((module) => (
                        <label key={module.key} className="flex items-center gap-2 text-sm text-muted-foreground">
                          <input
                            type="checkbox"
                            checked={selectedOrganizationModules?.orderedModuleKeys.includes(module.key) ?? false}
                            onChange={() => toggleOrganizationModule(module.key)}
                            className="h-4 w-4 rounded border-border"
                          />
                          {module.label}
                        </label>
                      ))
                    ) : (
                      <span className="text-xs text-muted-foreground">
                        This organization has no modules assigned in Organizations → Modules.
                      </span>
                    )}
                  </div>

                  <SortableChildTable<ModuleOption>
                    items={selectedOrganizationModuleItems}
                    getId={(item) => item.key}
                    columns={moduleColumns}
                    onReorder={(orderedIds) => updateOrganizationModules(selectedOrganizationId, orderedIds)}
                    primaryLabel="Primary"
                    emptyState="No modules selected for this organization."
                  />

                  {selectedOrganizationModules ? (
                    <div className="flex justify-end">
                      <Button type="button" variant="outline" size="sm" onClick={removeOrganizationMapping}>
                        Clear organization modules
                      </Button>
                    </div>
                  ) : null}
                </>
              ) : (
                <p className="text-xs text-muted-foreground">Select an organization to assign modules.</p>
              )}

              <div className="flex flex-wrap gap-2">
                {form.organizationModules.length > 0 ? (
                  form.organizationModules.map((entry) => (
                    <Badge key={entry.organizationId} variant="outline">
                      {organizationNameById.get(entry.organizationId) ?? entry.organizationId} · {entry.orderedModuleKeys.length}
                    </Badge>
                  ))
                ) : (
                  <span className="text-xs text-muted-foreground">No organization mappings configured.</span>
                )}
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
