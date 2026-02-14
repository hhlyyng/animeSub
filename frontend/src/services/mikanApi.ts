import type {
  DownloadTorrentRequest,
  MikanFeedResponse,
  MikanSearchResult,
  ParsedRssItem,
  TorrentInfo,
} from "../types/mikan";
import { API_BASE_URL } from "../config/env";
import { authFetch } from "./apiClient";

const API_BASE = API_BASE_URL;

async function resolveApiErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const payload = await response.json();
    if (typeof payload?.message === "string" && payload.message.trim().length > 0) {
      return payload.message.trim();
    }
  } catch {
    // Ignore non-JSON response and keep fallback.
  }

  return fallback;
}

export async function searchMikanAnime(
  title: string,
  bangumiId?: string,
  season?: number
): Promise<MikanSearchResult> {
  const params = new URLSearchParams({ title });
  if (bangumiId) {
    params.append("bangumiId", bangumiId);
  }
  if (typeof season === "number" && season > 0) {
    params.append("season", season.toString());
  }

  const response = await authFetch(`${API_BASE}/mikan/search?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`Failed to search: ${response.statusText}`);
  }
  return response.json();
}

export async function getMikanFeed(mikanId: string, bangumiId?: string): Promise<MikanFeedResponse> {
  const params = new URLSearchParams({ mikanId });
  if (bangumiId) {
    params.append("bangumiId", bangumiId);
  }

  const response = await authFetch(`${API_BASE}/mikan/feed?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`Failed to fetch feed: ${response.statusText}`);
  }
  return response.json();
}

export async function filterMikanFeed(
  mikanId: string,
  resolution?: string,
  subgroup?: string,
  subtitleType?: string
): Promise<ParsedRssItem[]> {
  const params = new URLSearchParams({ mikanId });
  if (resolution) params.append("resolution", resolution);
  if (subgroup) params.append("subgroup", subgroup);
  if (subtitleType) params.append("subtitleType", subtitleType);

  const response = await authFetch(`${API_BASE}/mikan/filter?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`Failed to filter feed: ${response.statusText}`);
  }
  return response.json();
}

export async function downloadTorrent(request: DownloadTorrentRequest): Promise<string> {
  const response = await authFetch(`${API_BASE}/mikan/download`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    let message = "Failed to download torrent";
    try {
      const error = await response.json();
      if (typeof error?.message === "string" && error.message.length > 0) {
        message = error.message;
      }
    } catch {
      // Keep default error message when response is not JSON.
    }
    throw new Error(message);
  }

  try {
    const payload = await response.json();
    if (typeof payload?.hash === "string" && payload.hash.trim().length > 0) {
      return payload.hash.trim();
    }
  } catch {
    // Keep fallback to request hash.
  }

  return request.torrentHash?.trim() ?? "";
}

export async function getTorrents(): Promise<TorrentInfo[]> {
  const response = await authFetch(`${API_BASE}/mikan/torrents`);
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to get torrents: ${response.statusText}`));
  }
  return response.json();
}

export async function pauseTorrent(hash: string): Promise<void> {
  const response = await authFetch(`${API_BASE}/mikan/torrents/${encodeURIComponent(hash)}/pause`, {
    method: "POST",
  });
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to pause torrent: ${response.statusText}`));
  }
}

export async function resumeTorrent(hash: string): Promise<void> {
  const response = await authFetch(`${API_BASE}/mikan/torrents/${encodeURIComponent(hash)}/resume`, {
    method: "POST",
  });
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to resume torrent: ${response.statusText}`));
  }
}

export async function removeTorrent(hash: string, deleteFiles = false): Promise<void> {
  const params = new URLSearchParams({ deleteFiles: String(deleteFiles) });
  const response = await authFetch(
    `${API_BASE}/mikan/torrents/${encodeURIComponent(hash)}?${params.toString()}`,
    {
      method: "DELETE",
    }
  );
  if (!response.ok) {
    throw new Error(await resolveApiErrorMessage(response, `Failed to remove torrent: ${response.statusText}`));
  }
}

export async function checkDownloadStatus(items: ParsedRssItem[]): Promise<Map<string, boolean>> {
  let torrents: TorrentInfo[] = [];
  try {
    torrents = await getTorrents();
  } catch (error) {
    console.warn("Failed to query qBittorrent status, falling back to local default:", error);
  }
  const existingHashes = new Set(
    torrents
      .map((torrent) => torrent.hash.trim().toUpperCase())
      .filter((hash) => hash.length > 0)
  );

  const statusEntries = items
    .map((item) => {
      const hash = item.torrentHash?.trim().toUpperCase();
      if (!hash) {
        return null;
      }

      return [hash, existingHashes.has(hash)] as const;
    })
    .filter((entry): entry is readonly [string, boolean] => entry !== null);

  return new Map(statusEntries);
}

export function startProgressPolling(
  intervalMs = 5000,
  onProgress?: (progresses: Map<string, TorrentInfo>) => void
): () => void {
  let isPolling = true;

  const poll = async () => {
    try {
      const torrents = await getTorrents();
      if (!isPolling) return;

      if (onProgress) {
        const entries = torrents
          .map((torrent) => {
            const hash = torrent.hash?.trim().toUpperCase();
            if (!hash) {
              return null;
            }

            return [hash, { ...torrent, hash }] as const;
          })
          .filter((entry): entry is readonly [string, TorrentInfo] => entry !== null);

        onProgress(new Map(entries));
      }
    } catch (error) {
      console.error("Failed to poll torrent progress:", error);
    } finally {
      if (isPolling) {
        window.setTimeout(poll, intervalMs);
      }
    }
  };

  void poll();

  return () => {
    isPolling = false;
  };
}
