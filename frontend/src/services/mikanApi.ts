import type {
  DownloadTorrentRequest,
  MikanFeedResponse,
  MikanSearchResult,
  ParsedRssItem,
  TorrentInfo,
} from "../types/mikan";

const API_BASE = "http://localhost:5072/api";

export async function searchMikanAnime(title: string, bangumiId?: string): Promise<MikanSearchResult> {
  const params = new URLSearchParams({ title });
  if (bangumiId) {
    params.append("bangumiId", bangumiId);
  }

  const response = await fetch(`${API_BASE}/mikan/search?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`Failed to search: ${response.statusText}`);
  }
  return response.json();
}

export async function getMikanFeed(mikanId: string): Promise<MikanFeedResponse> {
  const response = await fetch(`${API_BASE}/mikan/feed?mikanId=${encodeURIComponent(mikanId)}`);
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

  const response = await fetch(`${API_BASE}/mikan/filter?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`Failed to filter feed: ${response.statusText}`);
  }
  return response.json();
}

export async function downloadTorrent(request: DownloadTorrentRequest): Promise<void> {
  const response = await fetch(`${API_BASE}/mikan/download`, {
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
}

export async function getTorrents(): Promise<TorrentInfo[]> {
  const response = await fetch(`${API_BASE}/mikan/torrents`);
  if (!response.ok) {
    throw new Error(`Failed to get torrents: ${response.statusText}`);
  }
  return response.json();
}

export async function checkDownloadStatus(items: ParsedRssItem[]): Promise<Map<string, boolean>> {
  const torrents = await getTorrents();
  const existingHashes = new Set(torrents.map((torrent) => torrent.hash));
  return new Map(items.map((item) => [item.torrentHash, existingHashes.has(item.torrentHash)]));
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
        onProgress(new Map(torrents.map((torrent) => [torrent.hash, torrent])));
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
