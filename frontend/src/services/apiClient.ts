import { API_BASE_URL } from "../config/env";
import { useAppStore } from "../stores/useAppStores";

function getApiBaseUrl(): string {
  // Strip trailing /api if present for auth endpoints
  return API_BASE_URL.replace(/\/api$/, "");
}

export function getAuthHeaders(): Record<string, string> {
  const token = useAppStore.getState().token;
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }
  return headers;
}

export async function authFetch(
  url: string,
  options: RequestInit = {}
): Promise<Response> {
  const token = useAppStore.getState().token;
  const headers = new Headers(options.headers);

  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(url, { ...options, headers });

  if (response.status === 401) {
    const { clearToken } = useAppStore.getState();
    clearToken();
    // Redirect to login if not already there
    if (!window.location.pathname.startsWith("/login") && !window.location.pathname.startsWith("/setup")) {
      window.location.href = "/login";
    }
  }

  return response;
}

export async function authFetchJson<T>(
  url: string,
  options: RequestInit = {}
): Promise<T> {
  if (!options.headers) {
    options.headers = { "Content-Type": "application/json" };
  }
  const response = await authFetch(url, options);
  if (!response.ok) {
    let message = `Request failed: ${response.statusText}`;
    try {
      const payload = await response.json();
      if (typeof payload?.error === "string" && payload.error.trim()) {
        message = payload.error.trim();
      } else if (typeof payload?.message === "string" && payload.message.trim()) {
        message = payload.message.trim();
      }
    } catch {
      // keep default message
    }
    throw new Error(message);
  }
  return response.json();
}

export { getApiBaseUrl };
