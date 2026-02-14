export interface SubscriptionItem {
  id: number;
  bangumiId: number;
  title: string;
  mikanBangumiId: string;
  subgroupId?: string | null;
  subgroupName?: string | null;
  keywordInclude?: string | null;
  keywordExclude?: string | null;
  isEnabled: boolean;
  lastCheckedAt?: string | null;
  lastDownloadAt?: string | null;
  downloadCount: number;
  createdAt: string;
  updatedAt: string;
  rssUrl: string;
}

export interface SubscriptionDownloadHistoryItem {
  id: number;
  subscriptionId: number;
  torrentUrl: string;
  torrentHash: string;
  title: string;
  fileSize?: number | null;
  status: string;
  errorMessage?: string | null;
  publishedAt: string;
  discoveredAt: string;
  downloadedAt?: string | null;
  source?: string;
  progress?: number;
  downloadSpeed?: number | null;
  eta?: number | null;
  numSeeds?: number | null;
  numLeechers?: number | null;
  lastSyncedAt?: string | null;
  fileSizeDisplay?: string | null;
}

export interface ManualDownloadAnimeItem {
  bangumiId: number;
  title: string;
  mikanBangumiId?: string | null;
  taskCount: number;
  lastTaskAt: string;
}
