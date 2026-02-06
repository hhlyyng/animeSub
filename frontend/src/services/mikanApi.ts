import type { ParsedRssItem, MikanFeedResponse, DownloadTorrentRequest } from "../types/mikan";

const API_BASE = 'http://localhost:5072/api';

export async function searchMikanAnime(title: string): Promise<MikanSearchResult> {
  const response = await fetch(`${API_BASE}/mikan/search?title=${encodeURIComponent(title)}`);
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
  if (resolution) params.append('resolution', resolution);
  if (subgroup) params.append('subgroup', subgroup);
  if (subtitleType) params.append('subtitleType', subtitleType);

  const response = await fetch(`${API_BASE}/mikan/filter?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`Failed to filter feed: ${response.statusText}`);
  }
  return response.json();
}

export async function downloadTorrent(request: DownloadTorrentRequest): Promise<void> {
  const response = await fetch(`${API_BASE}/mikan/download`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request)
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.message || 'Failed to download torrent');
  }
}

export async function getTorrents(): Promise<QBTorrentInfo[]> {
  const response = await fetch(`${API_BASE}/mikan/torrents`);
  if (!response.ok) {
    throw new Error(`Failed to get torrents: ${response.statusText}`);
  }
  return response.json();
}

export async function checkDownloadStatus(items: ParsedRssItem[]): Promise<Map<string, boolean>> {
  const torrents = await getTorrents();
  const existingHashes = new Map(torrents.map(t => [t.hash, true]));
  return new Map(items.map(item => [item.torrentHash, existingHashes.has(item.torrentHash)]));
}
