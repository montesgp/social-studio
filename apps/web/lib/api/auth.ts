import { apiRequest } from "@/lib/api/client";
import { TokenResponse } from "@/lib/auth/session";

export interface LoginPayload {
  email: string;
  password: string;
}

export interface ResetPasswordPayload {
  email: string;
  token: string;
  newPassword: string;
}

export async function login(payload: LoginPayload): Promise<TokenResponse> {
  return apiRequest<TokenResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function requestPasswordReset(email: string) {
  return apiRequest<void>("/api/auth/forgot-password", {
    method: "POST",
    body: JSON.stringify({ email })
  });
}

export async function resetPassword(payload: ResetPasswordPayload) {
  return apiRequest<void>("/api/auth/reset-password", {
    method: "POST",
    body: JSON.stringify({
      email: payload.email,
      token: payload.token,
      newPassword: payload.newPassword
    })
  });
}
