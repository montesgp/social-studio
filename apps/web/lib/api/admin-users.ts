import { apiRequest } from "@/lib/api/client";

export type UserStatus = "Active" | "Suspended";

export interface AdminUser {
  id: string;
  email: string;
  displayName: string | null;
  status: UserStatus;
  roles: string[];
}

export async function getAdminUsers(token: string): Promise<AdminUser[]> {
  return apiRequest<AdminUser[]>("/api/admin/users", { method: "GET" }, token);
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
