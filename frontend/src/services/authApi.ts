import { API_BASE_URL } from "../config/env";
import type {
  AuthStatus,
  LoginRequest,
  LoginResponse,
  SetupRequest,
} from "../types/auth";

const AUTH_BASE = `${API_BASE_URL}/auth`;

export async function getAuthStatus(token?: string | null): Promise<AuthStatus> {
  const headers: Record<string, string> = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }
  const response = await fetch(`${AUTH_BASE}/status`, { headers });
  if (!response.ok) {
    throw new Error("Failed to check auth status");
  }
  return response.json();
}

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const response = await fetch(`${AUTH_BASE}/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    let message = "Login failed";
    try {
      const payload = await response.json();
      if (typeof payload?.message === "string" && payload.message.trim()) {
        message = payload.message.trim();
      }
    } catch {
      // keep default message
    }
    throw new Error(message);
  }

  return response.json();
}

export async function setup(request: SetupRequest): Promise<void> {
  const response = await fetch(`${AUTH_BASE}/setup`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    let message = "Setup failed";
    try {
      const payload = await response.json();
      if (typeof payload?.message === "string" && payload.message.trim()) {
        message = payload.message.trim();
      }
    } catch {
      // keep default message
    }
    throw new Error(message);
  }
}

export async function changeCredentials(
  currentPassword: string,
  token: string,
  newPassword?: string,
  newUsername?: string,
): Promise<void> {
  const response = await fetch(`${AUTH_BASE}/change-credentials`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ currentPassword, newPassword, newUsername }),
  });

  if (!response.ok) {
    let message = "Failed to update credentials";
    try {
      const payload = await response.json();
      if (typeof payload?.message === "string" && payload.message.trim()) {
        message = payload.message.trim();
      }
    } catch {
      // keep default
    }
    throw new Error(message);
  }
}

export function getLoginBackgroundUrl(): string {
  return `${AUTH_BASE}/background`;
}

export async function uploadLoginBackground(file: File, token: string): Promise<void> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${AUTH_BASE}/background`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: formData,
  });

  if (!response.ok) {
    throw new Error("Failed to upload background");
  }
}

export async function deleteLoginBackground(token: string): Promise<void> {
  const response = await fetch(`${AUTH_BASE}/background`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!response.ok) {
    throw new Error("Failed to delete background");
  }
}
