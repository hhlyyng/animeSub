import type { MikanSeasonInfo, ParsedRssItem, MikanFeedResponse } from "../../types/mikan";

export type DownloadPreferences = {
  resolution: '1080p' | '720p' | '4K' | 'all';
  subgroup: string;
  subtitleType: '简日内嵌' | '繁日' | '简体' | '繁体' | 'all';
}

export interface MikanSeasonInfo {
  seasonName: string;
  mikanBangumiId: string;
  year: number;
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
}

export interface ParsedRssItem {
  title: string;
  torrentUrl: string;
  magnetLink: string;
  torrentHash: string;
  publishedAt: string;
  resolution: string | null;
  subgroup: string | null;
  subtitleType: string | null;
  episode: number | null;
}

export interface DownloadPreferences {
  resolution: '1080p' | '720p' | '4K' | 'all';
  subgroup: string;
  subtitleType: '简日内嵌' | '繁日' | '简体' | '繁体' | 'all';
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
