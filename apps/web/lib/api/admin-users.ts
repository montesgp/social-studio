import { apiRequest } from "@/lib/api/client";

export type UserStatus = "Active" | "Suspended";

export interface AdminUser {
  id: string;
  email: string;
  displayName: string | null;
  status: UserStatus;
  roles: string[];
}

export interface CreateAdminUserPayload {
  email: string;
  displayName?: string | null;
  password: string;
  status: UserStatus;
  roles: string[];
}

export interface UpdateAdminUserPayload {
  email: string;
  displayName?: string | null;
  status: UserStatus;
  roles: string[];
}

export async function getAdminUsers(token: string): Promise<AdminUser[]> {
  return apiRequest<AdminUser[]>("/api/admin/users", { method: "GET" }, token);
}

export async function getAdminUser(id: string, token: string): Promise<AdminUser> {
  return apiRequest<AdminUser>(`/api/admin/users/${id}`, { method: "GET" }, token);
}

export async function createAdminUser(payload: CreateAdminUserPayload, token: string): Promise<AdminUser> {
  return apiRequest<AdminUser>(
    "/api/admin/users",
    { method: "POST", body: JSON.stringify(payload) },
    token
  );
}

export async function updateAdminUser(id: string, payload: UpdateAdminUserPayload, token: string): Promise<AdminUser> {
  return apiRequest<AdminUser>(
    `/api/admin/users/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}

export async function setAdminUserStatus(
  id: string,
  status: UserStatus,
  token: string
): Promise<void> {
  return apiRequest<void>(
    `/api/admin/users/${id}/status`,
    { method: "PUT", body: JSON.stringify({ status }) },
    token
  );
}
