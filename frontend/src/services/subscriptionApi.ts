import { API_BASE_URL } from "../config/env";
import { authFetch } from "./apiClient";
import type {
  ManualDownloadAnimeItem,
  SubscriptionDownloadHistoryItem,
  SubscriptionItem,
} from "../types/subscription";

const API_BASE = `${API_BASE_URL}/subscription`;

async function resolveApiErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const payload = await response.json();
    if (typeof payload?.message === "string" && payload.message.trim().length > 0) {
      return payload.message.trim();
    }
  } catch {
    // Keep fallback.
  }

  return fallback;
}

export async function getSubscriptions(): Promise<SubscriptionItem[]> {
  const response = await authFetch(API_BASE);
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to get subscriptions: ${response.statusText}`));
  }
  return response.json();
}

export type EnsureSubscriptionRequest = {
  bangumiId: number;
  title: string;
  mikanBangumiId: string;
};

export type CancelSubscriptionAction = "delete_files" | "keep_files";
export type CancelSubscriptionResponse = {
  subscriptionId: number;
  isEnabled: boolean;
  action: CancelSubscriptionAction;
  totalTorrents: number;
  processedCount: number;
  failedCount: number;
};

export async function getSubscriptionByBangumiId(bangumiId: number): Promise<SubscriptionItem | null> {
  const response = await authFetch(`${API_BASE}/bangumi/${bangumiId}`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(
      await resolveApiErrorMessage(response, `Failed to get subscription by bangumi: ${response.statusText}`)
    );
  }
  return response.json();
}

export async function ensureSubscription(request: EnsureSubscriptionRequest): Promise<SubscriptionItem> {
  const response = await authFetch(`${API_BASE}/ensure`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to ensure subscription: ${response.statusText}`));
  }

  return response.json();
}

export async function cancelSubscription(
  subscriptionId: number,
  action: CancelSubscriptionAction
): Promise<CancelSubscriptionResponse> {
  const response = await authFetch(`${API_BASE}/${subscriptionId}/cancel`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ action }),
  });

  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to cancel subscription: ${response.statusText}`));
  }

  return response.json();
}

export async function getSubscriptionHistory(
  subscriptionId: number,
  limit = 200
): Promise<SubscriptionDownloadHistoryItem[]> {
  const params = new URLSearchParams({ limit: String(limit) });
  const response = await authFetch(`${API_BASE}/${subscriptionId}/history?${params.toString()}`);
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to get history: ${response.statusText}`));
  }
  return response.json();
}

export async function getManualDownloadAnimes(limit = 200): Promise<ManualDownloadAnimeItem[]> {
  const params = new URLSearchParams({ limit: String(limit) });
  const response = await authFetch(`${API_BASE}/manual-anime?${params.toString()}`);
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to get manual anime: ${response.statusText}`));
  }
  return response.json();
}

export async function getManualAnimeHistory(
  bangumiId: number,
  limit = 200
): Promise<SubscriptionDownloadHistoryItem[]> {
  const params = new URLSearchParams({ limit: String(limit) });
  const response = await authFetch(`${API_BASE}/manual-anime/${bangumiId}/history?${params.toString()}`);
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to get manual history: ${response.statusText}`));
  }
  return response.json();
}
