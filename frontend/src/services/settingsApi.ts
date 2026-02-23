import { API_BASE_URL } from "../config/env";
import { authFetch } from "./apiClient";
import type {
  SettingsProfile,
  SettingsTestResponse,
  TestQbittorrentRequest,
  UpdateSettingsProfileRequest,
} from "../types/settings";

const API_BASE = `${API_BASE_URL}/settings`;

async function resolveApiErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const payload = await response.json();
    if (typeof payload?.error === "string" && payload.error.trim().length > 0) {
      return payload.error.trim();
    }
    if (typeof payload?.message === "string" && payload.message.trim().length > 0) {
      return payload.message.trim();
    }
  } catch {
    // Keep fallback.
  }
  return fallback;
}

export async function getSettingsProfile(): Promise<SettingsProfile> {
  const response = await authFetch(`${API_BASE}/profile`);
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to get settings profile: ${response.statusText}`));
  }
  return (await response.json()) as SettingsProfile;
}

export async function updateSettingsProfile(request: UpdateSettingsProfileRequest): Promise<void> {
  const response = await authFetch(`${API_BASE}/profile`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to save settings: ${response.statusText}`));
  }
}

export async function testTmdbToken(tmdbToken: string): Promise<SettingsTestResponse> {
  const response = await fetch(`${API_BASE}/test/tmdb`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ tmdbToken }),
  });

  const payload = (await response.json()) as SettingsTestResponse;
  if (!response.ok) {
    throw new Error(payload.message || "TMDB token test failed");
  }
  return payload;
}

export async function testQbittorrentField(request: TestQbittorrentRequest): Promise<SettingsTestResponse> {
  const response = await fetch(`${API_BASE}/test/qbittorrent`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  const payload = (await response.json()) as SettingsTestResponse;
  if (!response.ok) {
    throw new Error(payload.message || "qBittorrent field test failed");
  }
  return payload;
}

export async function testMikanPollingInterval(pollingIntervalMinutes: number): Promise<SettingsTestResponse> {
  const response = await fetch(`${API_BASE}/test/mikan-polling`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ pollingIntervalMinutes }),
  });

  const payload = (await response.json()) as SettingsTestResponse;
  if (!response.ok) {
    throw new Error(payload.message || "Mikan polling interval test failed");
  }
  return payload;
}
