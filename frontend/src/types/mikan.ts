export interface TorrentInfo {
  hash: string;
  name: string;
  size: number;
  state: string;
  progress: number;
  downloadSpeed?: number;
  eta?: number;
  numSeeds?: number;
  numLeechers?: number;
}

export interface MikanSeasonInfo {
  seasonName: string;
  mikanBangumiId: string;
  year: number;
  seasonNumber?: number;
}

export interface MikanSearchResult {
  animeTitle: string;
  seasons: MikanSeasonInfo[];
  defaultSeason: number;
}

export interface MikanFeedResponse {
  seasonName: string;
  items: ParsedRssItem[];
  availableSubgroups: string[];
  availableResolutions: string[];
  availableSubtitleTypes: string[];
  latestEpisode?: number | null;
  latestPublishedAt?: string | null;
  latestTitle?: string | null;
  episodeOffset?: number;
}

export interface ParsedRssItem {
  title: string;
  torrentUrl: string;
  magnetLink: string;
  torrentHash: string;
  canDownload?: boolean;
  fileSize?: number;
  formattedSize?: string;
  publishedAt: string;
  resolution: string | null;
  subgroup: string | null;
  subtitleType: string | null;
  episode: number | null;
  isCollection?: boolean;
}

export interface DownloadPreferences {
  resolution: "1080p" | "720p" | "4K" | "all";
  subgroup: string;
  subtitleType: string;
}

export interface DownloadTorrentRequest {
  magnetLink: string;
  torrentUrl?: string;
  title: string;
  torrentHash: string;
}

export interface QBTorrentInfo {
  hash: string;
  name: string;
  size: number;
  state: string;
  progress: number;
}
