import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ChangeEvent } from "react";
import { createPortal } from "react-dom";
import toast from "react-hot-toast";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";
import type { MikanSeasonInfo, ParsedRssItem, TorrentInfo } from "../../../types/mikan";
import * as mikanApi from "../../../services/mikanApi";
import * as subscriptionApi from "../../../services/subscriptionApi";
import * as settingsApi from "../../../services/settingsApi";
import type { DownloadPreferenceField } from "../../../types/settings";
import { StarIcon } from "../../../components/icons/StarIcon";
import { CloseIcon } from "../../../components/icons/CloseIcon";
import { BellIcon } from "../../../components/icons/BellIcon";
import { CheckIcon } from "../../../components/icons/CheckIcon";
import { ExternalLinkIcon } from "../../../components/icons/ExternalLinkIcon";
import { DownloadEpisodeGroup } from "./DownloadEpisodeGroup";
import type { DownloadActionState } from "./DownloadActionButton";
import { LoadingSpinner } from "../../../components/common/LoadingSpinner";
import { ErrorMessage } from "../../../components/common/ErrorMessage";

type AnimeDetailModalProps = {
  anime: AnimeInfo;
  open: boolean;
  onClose: () => void;
};

type EpisodeGroup = {
  type: "collection" | "episode" | "unknown";
  episode: number | null;
  items: ParsedRssItem[];
};

type PendingStateOverride = {
  state: Extract<DownloadActionState, "downloading" | "paused">;
  expiresAt: number;
};

type DownloadTaskFilter = "all" | "downloading" | "completed";
type SubscriptionPreferenceState = {
  subgroup: string;
  resolution: string;
  subtitleType: string;
  priorityOrder: DownloadPreferenceField[];
};
type ConfirmDialogAction =
  | { type: "download"; item: ParsedRssItem }
  | { type: "remove"; hash: string; title: string }
  | {
      type: "subscribe";
      title: string;
      subgroup: string;
      resolution: string;
      subtitleType: string;
      bestMatchHint?: string;
    }
  | { type: "unsubscribe" };

type CancelSubscriptionAction = "delete_files" | "keep_files";

const ACTION_OVERRIDE_TTL_MS = 8000;
const NO_SUBTITLE_FILTER_VALUE = "__NO_SUBTITLE__";
const DEFAULT_SUBSCRIPTION_PREFERENCE: SubscriptionPreferenceState = {
  subgroup: "all",
  resolution: "1080P",
  subtitleType: "\u7b80\u65e5\u5185\u5d4c",
  priorityOrder: ["subgroup", "resolution", "subtitleType"],
};

function getEpisodeGroupKey(group: EpisodeGroup): string {
  if (group.type === "collection") return "ep-collection";
  if (group.episode === null) return "ep-unknown";
  return `ep-${group.episode}`;
}

function normalizeTorrentState(state?: string): string {
  return (state ?? "").toLowerCase();
}

function isTorrentPausedState(state?: string): boolean {
  const normalized = normalizeTorrentState(state);
  return (
    normalized.includes("paused") ||
    normalized.includes("stopped") ||
    normalized.includes("stop") ||
    normalized.includes("stalldl") ||
    normalized.includes("queueddl") ||
    normalized.includes("pending")
  );
}

function isTorrentDownloadingState(state?: string): boolean {
  const normalized = normalizeTorrentState(state);
  return (
    normalized.includes("downloading") ||
    normalized.includes("forceddl") ||
    normalized.includes("metadl") ||
    normalized.includes("allocating") ||
    normalized.includes("checkingdl")
  );
}

function isTorrentCompletedState(state?: string, progress?: number): boolean {
  if (typeof progress === "number" && progress >= 99.9) {
    return true;
  }

  const normalized = normalizeTorrentState(state);
  return (
    normalized.includes("completed") ||
    normalized.includes("uploading") ||
    normalized.includes("forcedup") ||
    normalized.includes("checkingup") ||
    normalized.includes("stalledup") ||
    normalized.includes("queuedup")
  );
}

function parseSeasonNumber(text?: string | null): number | null {
  if (!text) return null;

  const patterns = [
    /season\s*(\d+)/i,
    /\bs\s*0?(\d+)\b/i,
    /(\d+)(?:st|nd|rd|th)\s*season/i,
    /\u7b2c\s*([0-9]+)\s*\u5b63/i,
    /\u7b2c\s*([\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]+)\s*\u5b63/i,
  ];

  for (const pattern of patterns) {
    const match = text.match(pattern);
    if (!match?.[1]) continue;

    const raw = match[1];
    const numeric = Number.parseInt(raw, 10);
    if (!Number.isNaN(numeric) && numeric > 0) return numeric;

    const chineseMap: Record<string, number> = {
      "\u4e00": 1,
      "\u4e8c": 2,
      "\u4e09": 3,
      "\u56db": 4,
      "\u4e94": 5,
      "\u516d": 6,
      "\u4e03": 7,
      "\u516b": 8,
      "\u4e5d": 9,
      "\u5341": 10,
    };
    if (raw in chineseMap) return chineseMap[raw];
  }

  return null;
}

function detectExplicitSeason(anime: AnimeInfo): number | null {
  return (
    parseSeasonNumber(anime.ch_title) ??
    parseSeasonNumber(anime.en_title) ??
    parseSeasonNumber(anime.jp_title)
  );
}

function uniqueSorted(values: Array<string | null | undefined>): string[] {
  return Array.from(
    new Set(
      values
        .map((value) => value?.trim() ?? "")
        .filter((value) => value.length > 0)
    )
  ).sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));
}

function normalizeSubtitleFilterValue(value: string | null | undefined): string {
  const normalized = value?.trim() ?? "";
  return normalized.length > 0 ? normalized : NO_SUBTITLE_FILTER_VALUE;
}

function resolvePreferenceDisplayValue(
  value: string,
  noPreferenceLabel: string,
  noSubtitleLabel: string
): string {
  if (!value || value === "all") {
    return noPreferenceLabel;
  }
  if (value === NO_SUBTITLE_FILTER_VALUE) {
    return noSubtitleLabel;
  }
  return value;
}

function normalizePreferenceCompareValue(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function resolveItemPreferenceValue(item: ParsedRssItem, field: DownloadPreferenceField): string {
  if (field === "subgroup") {
    return (item.subgroup ?? "").trim();
  }
  if (field === "resolution") {
    return (item.resolution ?? "").trim();
  }
  return normalizeSubtitleFilterValue(item.subtitleType);
}

function isPreferenceMatched(
  item: ParsedRssItem,
  preferences: SubscriptionPreferenceState,
  field: DownloadPreferenceField
): boolean {
  const preferred = normalizePreferenceCompareValue(preferences[field]);
  if (!preferred || preferred === "all") {
    return true;
  }

  const actual = normalizePreferenceCompareValue(resolveItemPreferenceValue(item, field));
  return actual === preferred;
}

function findBestPreferenceMatch(
  items: ParsedRssItem[],
  preferences: SubscriptionPreferenceState
): ParsedRssItem | null {
  if (items.length === 0) {
    return null;
  }

  const order = preferences.priorityOrder.length > 0 ? preferences.priorityOrder : DEFAULT_SUBSCRIPTION_PREFERENCE.priorityOrder;
  const scored = items
    .map((item) => {
      let score = 0;
      let matchedCount = 0;

      order.forEach((field, index) => {
        const preferred = normalizePreferenceCompareValue(preferences[field]);
        if (!preferred || preferred === "all") {
          return;
        }
        if (isPreferenceMatched(item, preferences, field)) {
          score += (order.length - index) * 100;
          matchedCount += 1;
        }
      });

      return {
        item,
        score,
        matchedCount,
        publishedAt: Date.parse(item.publishedAt) || 0,
      };
    })
    .filter((entry) => entry.matchedCount > 0)
    .sort((a, b) => b.score - a.score || b.matchedCount - a.matchedCount || b.publishedAt - a.publishedAt);

  return scored[0]?.item ?? null;
}

function pickSeasonByNumber(seasons: MikanSeasonInfo[], expected: number): MikanSeasonInfo[] {
  const matched = seasons.filter((season) => {
    if (typeof season.seasonNumber === "number") {
      return season.seasonNumber === expected;
    }
    return parseSeasonNumber(season.seasonName) === expected;
  });

  if (matched.length === 0) return [];
  return matched.sort((a, b) => (b.year ?? 0) - (a.year ?? 0));
}

function groupItemsByEpisode(items: ParsedRssItem[]): EpisodeGroup[] {
  const groups = new Map<string, EpisodeGroup>();

  for (const item of items) {
    const isCollection = item.isCollection === true;
    const type = isCollection ? "collection" : item.episode !== null ? "episode" : "unknown";
    const key = type === "episode" ? `ep-${item.episode}` : type;
    const existing = groups.get(key);
    if (existing) {
      existing.items.push(item);
      continue;
    }

    groups.set(key, {
      type,
      episode: item.episode,
      items: [item],
    });
  }

  return Array.from(groups.values())
    .map((group) => ({
      ...group,
      items: group.items.sort((a, b) => new Date(b.publishedAt).getTime() - new Date(a.publishedAt).getTime()),
    }))
    .sort((a, b) => {
      if (a.type === b.type) {
        if (a.type === "episode" && a.episode !== null && b.episode !== null) {
          return b.episode - a.episode;
        }
        return 0;
      }

      if (a.type === "collection") return -1;
      if (b.type === "collection") return 1;
      if (a.type === "episode" && b.type === "unknown") return -1;
      if (a.type === "unknown" && b.type === "episode") return 1;

      return 0;
    });
}

export function AnimeDetailModal({ anime, open, onClose }: AnimeDetailModalProps) {
  const language = useAppStore((state) => state.language);
  const downloadPreferences = useAppStore((state) => state.downloadPreferences);
  const setDownloadPreferences = useAppStore((state) => state.setDownloadPreferences);
  const setDownloadLoading = useAppStore((state) => state.setDownloadLoading);

  const [seasons, setSeasons] = useState<MikanSeasonInfo[]>([]);
  const [selectedSeasonIndex, setSelectedSeasonIndex] = useState(0);
  const [feedItems, setFeedItems] = useState<ParsedRssItem[]>([]);
  const [latestEpisode, setLatestEpisode] = useState<number | null>(null);
  const [latestPublishedAt, setLatestPublishedAt] = useState<string | null>(null);
  const [downloadStatus, setDownloadStatus] = useState<Map<string, boolean>>(new Map());
  const [torrentInfo, setTorrentInfo] = useState<Map<string, TorrentInfo>>(new Map());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busyHash, setBusyHash] = useState<string | null>(null);
  const [taskFilter, setTaskFilter] = useState<DownloadTaskFilter>("all");
  const [collapsedGroupKeys, setCollapsedGroupKeys] = useState<Set<string>>(new Set());
  const [stateOverrides, setStateOverrides] = useState<Map<string, PendingStateOverride>>(new Map());
  const [confirmDialog, setConfirmDialog] = useState<ConfirmDialogAction | null>(null);
  const [confirmBusy, setConfirmBusy] = useState(false);
  const [isSubscribed, setIsSubscribed] = useState(false);
  const [currentSubscriptionId, setCurrentSubscriptionId] = useState<number | null>(null);
  const [isSubscribeBusy, setIsSubscribeBusy] = useState(false);
  const [subscriptionPreference, setSubscriptionPreference] = useState<SubscriptionPreferenceState>(
    DEFAULT_SUBSCRIPTION_PREFERENCE
  );
  const stopPollingRef = useRef<(() => void) | null>(null);
  const latestFeedRequestIdRef = useRef(0);

  const explicitSeasonNumber = useMemo(() => detectExplicitSeason(anime), [anime]);
  const shouldShowSeasonSelector = explicitSeasonNumber === null && seasons.length > 1;

  const primaryTitle =
    language === "zh"
      ? anime.ch_title || anime.en_title || anime.jp_title || "Unknown Title"
      : anime.en_title || anime.ch_title || anime.jp_title || "Unknown Title";

  const secondaryTitle = anime.jp_title !== primaryTitle ? anime.jp_title : null;

  const hasChDesc = Boolean(anime.ch_desc && anime.ch_desc.trim().length > 0);
  const hasEnDesc = Boolean(anime.en_desc && anime.en_desc.trim().length > 0);

  const description =
    language === "zh"
      ? hasChDesc
        ? anime.ch_desc
        : hasEnDesc
          ? anime.en_desc
          : "\u6682\u65e0\u63cf\u8ff0"
      : hasEnDesc
        ? anime.en_desc
        : hasChDesc
          ? anime.ch_desc
          : "No description available";

  const descriptionLabel = language === "zh" ? "\u7b80\u4ecb" : "Synopsis";
  const linksLabel = language === "zh" ? "\u76f8\u5173\u94fe\u63a5" : "Links";
  const downloadLabel = language === "zh" ? "\u4e0b\u8f7d\u6e90" : "Download Sources";
  const noDownloadLabel = language === "zh" ? "\u6682\u65e0\u53ef\u7528\u4e0b\u8f7d\u6e90" : "No download sources available";
  const latestEpisodeLabel = language === "zh" ? "\u6700\u65b0\u66f4\u65b0" : "Latest update";
  const seasonLabel = language === "zh" ? "\u5b63\u5ea6" : "Season";
  const resolutionLabel = language === "zh" ? "\u5206\u8fa8\u7387" : "Resolution";
  const statusLabel = language === "zh" ? "\u4e0b\u8f7d\u72b6\u6001" : "Task Status";
  const statusDownloadingLabel = language === "zh" ? "\u4e0b\u8f7d\u4e2d" : "Downloading";
  const statusCompletedLabel = language === "zh" ? "\u4e0b\u8f7d\u5b8c\u6210" : "Completed";
  const subgroupLabel = language === "zh" ? "\u5b57\u5e55\u7ec4" : "Subgroup";
  const subtitleLabel = language === "zh" ? "\u5b57\u5e55" : "Subtitle";
  const noSubtitleLabel = language === "zh" ? "\u65e0\u5b57\u5e55" : "No subtitle";
  const allLabel = language === "zh" ? "\u5168\u90e8" : "All";
  const noPreferenceLabel = language === "zh" ? "\u4e0d\u9650" : "Any";
  const cancelLabel = language === "zh" ? "\u53d6\u6d88" : "Cancel";
  const confirmActionLabel = language === "zh" ? "\u786e\u8ba4" : "Confirm";
  const confirmDownloadTitle = language === "zh" ? "\u786e\u8ba4\u63a8\u9001\u4e0b\u8f7d\u4efb\u52a1" : "Confirm download task";
  const confirmRemoveTitle = language === "zh" ? "\u786e\u8ba4\u5220\u9664\u4e0b\u8f7d\u4efb\u52a1" : "Confirm remove task";
  const confirmSubscribeTitle = language === "zh" ? "\u786e\u8ba4\u8ba2\u9605" : "Confirm subscription";
  const confirmUnsubscribeTitle = language === "zh" ? "\u786e\u8ba4\u53d6\u6d88\u8ba2\u9605" : "Confirm unsubscribe";
  const subscribeLabel = language === "zh" ? "\u8ba2\u9605" : "Subscribe";
  const unsubscribeLabel = language === "zh" ? "\u53d6\u6d88\u8ba2\u9605" : "Unsubscribe";
  const cancelAndDeleteLabel = language === "zh" ? "\u53d6\u8ba2\u5e76\u5220\u9664\u6587\u4ef6" : "Unsubscribe + Delete";
  const cancelAndKeepLabel = language === "zh" ? "\u53d6\u8ba2\u4f46\u4fdd\u7559\u6587\u4ef6" : "Unsubscribe + Keep";
  const rollbackLabel = language === "zh" ? "\u56de\u9000" : "Back";
  const subscribeExpandWidthClass = language === "zh" ? "group-hover/subscribe:w-20" : "group-hover/subscribe:w-24";
  const subscribeTextExpandClass = language === "zh" ? "group-hover/subscribe:max-w-12" : "group-hover/subscribe:max-w-16";

  const subscribeMikanBangumiId = useMemo(() => {
    return (
      anime.mikan_bangumi_id?.trim() ||
      seasons[selectedSeasonIndex]?.mikanBangumiId?.trim() ||
      seasons[0]?.mikanBangumiId?.trim() ||
      ""
    );
  }, [anime.mikan_bangumi_id, seasons, selectedSeasonIndex]);

  const subscribePreferencePreview = useMemo(
    () => ({
      subgroup: resolvePreferenceDisplayValue(subscriptionPreference.subgroup, noPreferenceLabel, noSubtitleLabel),
      resolution: resolvePreferenceDisplayValue(subscriptionPreference.resolution, noPreferenceLabel, noSubtitleLabel),
      subtitleType: resolvePreferenceDisplayValue(subscriptionPreference.subtitleType, noPreferenceLabel, noSubtitleLabel),
    }),
    [
      noPreferenceLabel,
      noSubtitleLabel,
      subscriptionPreference.resolution,
      subscriptionPreference.subgroup,
      subscriptionPreference.subtitleType,
    ]
  );

  const loadSeasons = useCallback(async () => {
    let shouldKeepLoadingForFeed = false;
    setLoading(true);
    setError(null);

    if (anime.mikan_bangumi_id) {
      setSeasons([
        {
          seasonName: "Season",
          mikanBangumiId: anime.mikan_bangumi_id,
          year: 0,
          seasonNumber: explicitSeasonNumber ?? undefined,
        },
      ]);
      setSelectedSeasonIndex(0);
      shouldKeepLoadingForFeed = true;
      return;
    }

    const searchTitle = anime.ch_title || anime.en_title || anime.jp_title;
    if (!searchTitle) {
      setError(language === "zh" ? "\u6ca1\u6709\u53ef\u641c\u7d22\u7684\u6807\u9898" : "No title available for search");
      setLoading(false);
      return;
    }

    try {
      const result = await mikanApi.searchMikanAnime(searchTitle, anime.bangumi_id, explicitSeasonNumber ?? undefined);
      if (!result?.seasons?.length) {
        setError(language === "zh" ? "\u672a\u627e\u5230\u5339\u914d\u7684\u52a8\u753b" : "No matching anime found");
        return;
      }

      let finalSeasons = result.seasons;
      let finalIndex = Math.min(Math.max(result.defaultSeason ?? 0, 0), result.seasons.length - 1);

      if (explicitSeasonNumber !== null) {
        const matched = pickSeasonByNumber(result.seasons, explicitSeasonNumber);
        if (matched.length > 0) {
          finalSeasons = [matched[0]];
          finalIndex = 0;
        } else {
          finalSeasons = [result.seasons[finalIndex]];
          finalIndex = 0;
        }
      }

      setSeasons(finalSeasons);
      setSelectedSeasonIndex(finalIndex);
      shouldKeepLoadingForFeed = finalSeasons.length > 0;
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to search";
      setError(errorMessage);
    } finally {
      if (!shouldKeepLoadingForFeed) {
        setLoading(false);
      }
    }
  }, [anime, explicitSeasonNumber, language]);

  const loadFeed = useCallback(async () => {
    if (seasons.length === 0) return;

    const season = seasons[selectedSeasonIndex];
    if (!season) return;
    const requestId = latestFeedRequestIdRef.current + 1;
    latestFeedRequestIdRef.current = requestId;

    try {
      setLoading(true);
      setError(null);

      const feed = await mikanApi.getMikanFeed(season.mikanBangumiId, anime.bangumi_id);
      if (requestId !== latestFeedRequestIdRef.current) {
        return;
      }

      setFeedItems(feed.items);
      setLatestEpisode(feed.latestEpisode ?? null);
      setLatestPublishedAt(feed.latestPublishedAt ?? null);
      setCollapsedGroupKeys(new Set());
      setTaskFilter("all");
      setDownloadStatus(new Map());
      setTorrentInfo(new Map());
      setStateOverrides(new Map());

      // Keep cross-anime preference for resolution, reset scoped filters to avoid stale invalid options.
      setDownloadPreferences({ subgroup: "all", subtitleType: "all" });

      stopPollingRef.current?.();
      stopPollingRef.current = mikanApi.startProgressPolling(3000, (progresses: Map<string, TorrentInfo>) => {
        if (requestId !== latestFeedRequestIdRef.current) {
          return;
        }

        setTorrentInfo(progresses);
        setStateOverrides((prev) => {
          if (prev.size === 0) {
            return prev;
          }

          const now = Date.now();
          let changed = false;
          const next = new Map(prev);

          for (const [hash, override] of prev.entries()) {
            const info = progresses.get(hash);
            const expired = override.expiresAt <= now;

            if (!info) {
              if (expired) {
                next.delete(hash);
                changed = true;
              }
              continue;
            }

            const confirmed =
              override.state === "downloading"
                ? isTorrentDownloadingState(info.state) || info.progress >= 99.9
                : isTorrentPausedState(info.state);

            if (confirmed || expired) {
              next.delete(hash);
              changed = true;
            }
          }

          return changed ? next : prev;
        });
      });

      void mikanApi
        .checkDownloadStatus(feed.items)
        .then((status) => {
          if (requestId !== latestFeedRequestIdRef.current) {
            return;
          }
          setDownloadStatus(status);
        })
        .catch((statusError) => {
          console.warn("Failed to query initial download status:", statusError);
        });
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to load feed";
      setError(errorMessage);
    } finally {
      if (requestId === latestFeedRequestIdRef.current) {
        setLoading(false);
      }
    }
  }, [anime.bangumi_id, seasons, selectedSeasonIndex, setDownloadPreferences]);

  const resolutionOptions = useMemo(() => uniqueSorted(feedItems.map((item) => item.resolution)), [feedItems]);

  const subgroupOptions = useMemo(() => {
    const candidateItems = feedItems.filter((item) => {
      if (downloadPreferences.resolution !== "all" && item.resolution !== downloadPreferences.resolution) {
        return false;
      }
      if (
        downloadPreferences.subtitleType !== "all" &&
        normalizeSubtitleFilterValue(item.subtitleType) !== downloadPreferences.subtitleType
      ) {
        return false;
      }
      return true;
    });
    return uniqueSorted(candidateItems.map((item) => item.subgroup));
  }, [feedItems, downloadPreferences.resolution, downloadPreferences.subtitleType]);

  const subtitleTypeOptions = useMemo(() => {
    const candidateItems = feedItems.filter((item) => {
      if (downloadPreferences.resolution !== "all" && item.resolution !== downloadPreferences.resolution) {
        return false;
      }
      if (downloadPreferences.subgroup !== "all" && item.subgroup !== downloadPreferences.subgroup) {
        return false;
      }
      return true;
    });
    return uniqueSorted(candidateItems.map((item) => normalizeSubtitleFilterValue(item.subtitleType)));
  }, [feedItems, downloadPreferences.resolution, downloadPreferences.subgroup]);

  useEffect(() => {
    const nextPreferences: Partial<typeof downloadPreferences> = {};

    if (
      downloadPreferences.resolution !== "all" &&
      !resolutionOptions.includes(downloadPreferences.resolution)
    ) {
      nextPreferences.resolution = "all";
    }

    if (
      downloadPreferences.subgroup !== "all" &&
      !subgroupOptions.includes(downloadPreferences.subgroup)
    ) {
      nextPreferences.subgroup = "all";
    }

    if (
      downloadPreferences.subtitleType !== "all" &&
      !subtitleTypeOptions.includes(downloadPreferences.subtitleType)
    ) {
      nextPreferences.subtitleType = "all";
    }

    if (Object.keys(nextPreferences).length > 0) {
      setDownloadPreferences(nextPreferences);
    }
  }, [
    downloadPreferences.resolution,
    downloadPreferences.subgroup,
    downloadPreferences.subtitleType,
    resolutionOptions,
    subgroupOptions,
    subtitleTypeOptions,
    setDownloadPreferences,
  ]);

  const resolveItemTaskStatus = useCallback((item: ParsedRssItem): "idle" | "downloading" | "completed" => {
    const hash = item.torrentHash?.trim().toUpperCase() ?? "";
    if (!hash) {
      return "idle";
    }

    const override = stateOverrides.get(hash);
    if (override) {
      return "downloading";
    }

    const hasRecord = downloadStatus.get(hash) ?? false;
    if (!hasRecord) {
      return "idle";
    }

    const info = torrentInfo.get(hash);
    if (isTorrentCompletedState(info?.state, info?.progress)) {
      return "completed";
    }

    return "downloading";
  }, [downloadStatus, stateOverrides, torrentInfo]);

  const filteredItems = useMemo(
    () =>
      feedItems.filter((item) => {
        if (downloadPreferences.resolution !== "all" && item.resolution !== downloadPreferences.resolution) {
          return false;
        }
        if (downloadPreferences.subgroup !== "all" && item.subgroup !== downloadPreferences.subgroup) {
          return false;
        }
        if (
          downloadPreferences.subtitleType !== "all" &&
          normalizeSubtitleFilterValue(item.subtitleType) !== downloadPreferences.subtitleType
        ) {
          return false;
        }

        if (taskFilter !== "all") {
          const status = resolveItemTaskStatus(item);
          if (taskFilter === "downloading") {
            return status === "downloading";
          }
          return status === "completed";
        }

        return true;
      }),
    [feedItems, downloadPreferences, resolveItemTaskStatus, taskFilter]
  );

  const episodeGroups = useMemo(() => groupItemsByEpisode(filteredItems), [filteredItems]);

  const buttonStateOverrides = useMemo(() => {
    const next = new Map<string, DownloadActionState>();
    stateOverrides.forEach((value, hash) => {
      next.set(hash, value.state);
    });
    return next;
  }, [stateOverrides]);

  const toggleGroupCollapse = useCallback((groupKey: string) => {
    setCollapsedGroupKeys((prev) => {
      const next = new Set(prev);
      if (next.has(groupKey)) {
        next.delete(groupKey);
      } else {
        next.add(groupKey);
      }
      return next;
    });
  }, []);

  const applyStateOverride = useCallback((hash: string, state: PendingStateOverride["state"]) => {
    const normalizedHash = hash.trim().toUpperCase();
    if (!normalizedHash) return;

    setStateOverrides((prev) => {
      const next = new Map(prev);
      next.set(normalizedHash, {
        state,
        expiresAt: Date.now() + ACTION_OVERRIDE_TTL_MS,
      });
      return next;
    });
  }, []);

  const markTorrentState = useCallback((hash: string, patch: Partial<TorrentInfo>) => {
    const normalizedHash = hash.trim().toUpperCase();
    if (!normalizedHash) return;

    setTorrentInfo((prev) => {
      const next = new Map(prev);
      const existing = next.get(normalizedHash);
      if (!existing) return prev;
      next.set(normalizedHash, { ...existing, ...patch });
      return next;
    });
  }, []);

  const executeDownload = useCallback(
    async (item: ParsedRssItem) => {
      if (item.canDownload === false) {
        toast.error(language === "zh" ? "\u8d44\u6e90 hash \u7f3a\u5931\uff0c\u65e0\u6cd5\u4e0b\u8f7d" : "Torrent hash is missing, cannot download");
        return;
      }

      try {
        const requestHash = item.torrentHash?.trim().toUpperCase() ?? "";
        const parsedBangumiId = Number.parseInt(anime.bangumi_id, 10);
        if (requestHash.length > 0) {
          setBusyHash(requestHash);
        }
        setDownloadLoading(true);

        const qbHash = await mikanApi.downloadTorrent({
          magnetLink: item.magnetLink,
          torrentUrl: item.torrentUrl,
          title: item.title,
          torrentHash: requestHash,
          bangumiId: Number.isFinite(parsedBangumiId) && parsedBangumiId > 0 ? parsedBangumiId : undefined,
          mikanBangumiId: anime.mikan_bangumi_id,
          animeTitle: anime.ch_title || anime.en_title || anime.jp_title,
        });
        const effectiveHash = qbHash.trim().toUpperCase();
        if (effectiveHash.length === 0) {
          throw new Error("Missing normalized hash from backend response");
        }

        setDownloadStatus((prev) => {
          const next = new Map(prev);
          next.set(effectiveHash, true);
          return next;
        });

        setTorrentInfo((prev) => {
          if (prev.has(effectiveHash)) return prev;
          const next = new Map(prev);
          next.set(effectiveHash, {
            hash: effectiveHash,
            name: item.title,
            size: 0,
            state: "downloading",
            progress: 0,
          });
          return next;
        });
        applyStateOverride(effectiveHash, "downloading");

        toast.success(language === "zh" ? "\u4efb\u52a1\u63a8\u9001\u6210\u529f" : "Task queued successfully");
      } catch (err) {
        const errorMsg = err instanceof Error ? err.message : "Failed to download";
        toast.error(language === "zh" ? `\u4e0b\u8f7d\u5931\u8d25: ${errorMsg}` : `Download failed: ${errorMsg}`);
      } finally {
        setBusyHash(null);
        setDownloadLoading(false);
      }
    },
    [applyStateOverride, language, setDownloadLoading]
  );

  const handlePause = useCallback(async (hash: string) => {
    const normalizedHash = hash.trim().toUpperCase();
    try {
      setBusyHash(normalizedHash);
      await mikanApi.pauseTorrent(normalizedHash);
      markTorrentState(normalizedHash, { state: "pausedDL" });
      applyStateOverride(normalizedHash, "paused");
      toast.success(language === "zh" ? "\u5df2\u6682\u505c\u4e0b\u8f7d" : "Download paused");
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : "Failed to pause torrent";
      toast.error(language === "zh" ? `\u6682\u505c\u5931\u8d25: ${errorMsg}` : `Pause failed: ${errorMsg}`);
    } finally {
      setBusyHash(null);
    }
  }, [applyStateOverride, language, markTorrentState]);

  const handleResume = useCallback(async (hash: string) => {
    const normalizedHash = hash.trim().toUpperCase();
    try {
      setBusyHash(normalizedHash);
      await mikanApi.resumeTorrent(normalizedHash);
      markTorrentState(normalizedHash, { state: "downloading" });
      applyStateOverride(normalizedHash, "downloading");
      toast.success(language === "zh" ? "\u5df2\u7ee7\u7eed\u4e0b\u8f7d" : "Download resumed");
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : "Failed to resume torrent";
      toast.error(language === "zh" ? `\u7ee7\u7eed\u5931\u8d25: ${errorMsg}` : `Resume failed: ${errorMsg}`);
    } finally {
      setBusyHash(null);
    }
  }, [applyStateOverride, language, markTorrentState]);

  const executeRemove = useCallback(async (hash: string) => {
    const normalizedHash = hash.trim().toUpperCase();
    try {
      setBusyHash(normalizedHash);
      await mikanApi.removeTorrent(normalizedHash, false);

      setTorrentInfo((prev) => {
        const next = new Map(prev);
        next.delete(normalizedHash);
        return next;
      });

      setDownloadStatus((prev) => {
        const next = new Map(prev);
        next.set(normalizedHash, false);
        return next;
      });

      setStateOverrides((prev) => {
        if (!prev.has(normalizedHash)) return prev;
        const next = new Map(prev);
        next.delete(normalizedHash);
        return next;
      });

      toast.success(language === "zh" ? "\u4e0b\u8f7d\u8bb0\u5f55\u5df2\u79fb\u9664" : "Download removed");
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : "Failed to remove torrent";
      toast.error(language === "zh" ? `\u79fb\u9664\u5931\u8d25: ${errorMsg}` : `Remove failed: ${errorMsg}`);
    } finally {
      setBusyHash(null);
    }
  }, [language]);

  const requestDownload = useCallback(async (item: ParsedRssItem) => {
    if (item.canDownload === false) {
      toast.error(language === "zh" ? "\u8d44\u6e90 hash \u7f3a\u5931\uff0c\u65e0\u6cd5\u4e0b\u8f7d" : "Torrent hash is missing, cannot download");
      return;
    }
    setConfirmDialog({ type: "download", item });
  }, [language]);

  const requestRemove = useCallback(async (hash: string, title: string) => {
    const normalizedHash = hash.trim().toUpperCase();
    if (!normalizedHash) {
      return;
    }
    setConfirmDialog({ type: "remove", hash: normalizedHash, title });
  }, []);

  const handleSeasonChange = (event: ChangeEvent<HTMLSelectElement>) => {
    setSelectedSeasonIndex(Number.parseInt(event.target.value, 10));
  };

  const handlePreferenceChange = (key: keyof typeof downloadPreferences, value: string) => {
    setDownloadPreferences({ [key]: value });
  };

  const executeSubscribe = useCallback(async () => {
    const bangumiId = Number.parseInt(anime.bangumi_id, 10);
    if (!Number.isFinite(bangumiId) || bangumiId <= 0) {
      toast.error(language === "zh" ? "\u7f3a\u5c11\u6709\u6548 BangumiId\uff0c\u65e0\u6cd5\u8ba2\u9605" : "Missing valid BangumiId");
      return;
    }

    if (!subscribeMikanBangumiId) {
      toast.error(language === "zh" ? "\u7f3a\u5c11 Mikan ID\uff0c\u65e0\u6cd5\u8ba2\u9605" : "Missing Mikan ID");
      return;
    }

    if (isSubscribeBusy) {
      return;
    }

    try {
      setIsSubscribeBusy(true);
      const ensured = await subscriptionApi.ensureSubscription({
        bangumiId,
        title: anime.ch_title || anime.en_title || anime.jp_title || "Unknown Title",
        mikanBangumiId: subscribeMikanBangumiId,
      });

      setIsSubscribed(Boolean(ensured.isEnabled));
      setCurrentSubscriptionId(ensured.id);
      toast.success(language === "zh" ? "\u8ba2\u9605\u6210\u529f" : "Subscribed");
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to subscribe";
      toast.error(language === "zh" ? `\u8ba2\u9605\u5931\u8d25: ${message}` : `Subscribe failed: ${message}`);
    } finally {
      setIsSubscribeBusy(false);
    }
  }, [
    anime.bangumi_id,
    anime.ch_title,
    anime.en_title,
    anime.jp_title,
    isSubscribeBusy,
    language,
    subscribeMikanBangumiId,
  ]);

  const executeUnsubscribe = useCallback(async (action: CancelSubscriptionAction) => {
    if (isSubscribeBusy) {
      return;
    }

    if (!currentSubscriptionId || currentSubscriptionId <= 0) {
      toast.error(language === "zh" ? "\u8ba2\u9605\u8bb0\u5f55\u7f3a\u5931\uff0c\u65e0\u6cd5\u53d6\u6d88" : "Subscription record is missing");
      return;
    }

    try {
      setIsSubscribeBusy(true);
      const updated = await subscriptionApi.cancelSubscription(currentSubscriptionId, action);
      setIsSubscribed(Boolean(updated.isEnabled));
      setCurrentSubscriptionId(updated.subscriptionId);
      toast.success(language === "zh" ? "\u5df2\u53d6\u6d88\u8ba2\u9605" : "Subscription canceled");
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to cancel subscription";
      toast.error(language === "zh" ? `\u53d6\u6d88\u8ba2\u9605\u5931\u8d25: ${message}` : `Unsubscribe failed: ${message}`);
    } finally {
      setIsSubscribeBusy(false);
    }
  }, [currentSubscriptionId, isSubscribeBusy, language]);

  const handleSubscriptionButtonClick = useCallback(() => {
    if (isSubscribeBusy) {
      return;
    }

    if (isSubscribed) {
      setConfirmDialog({ type: "unsubscribe" });
      return;
    }

    const bangumiId = Number.parseInt(anime.bangumi_id, 10);
    if (!Number.isFinite(bangumiId) || bangumiId <= 0) {
      toast.error(language === "zh" ? "\u7f3a\u5c11\u6709\u6548 BangumiId\uff0c\u65e0\u6cd5\u8ba2\u9605" : "Missing valid BangumiId");
      return;
    }

    if (!subscribeMikanBangumiId) {
      toast.error(language === "zh" ? "\u7f3a\u5c11 Mikan ID\uff0c\u65e0\u6cd5\u8ba2\u9605" : "Missing Mikan ID");
      return;
    }

    const priorityOrder =
      subscriptionPreference.priorityOrder.length > 0
        ? subscriptionPreference.priorityOrder
        : DEFAULT_SUBSCRIPTION_PREFERENCE.priorityOrder;

    const hasActivePreferences = priorityOrder.some((field) => {
      const value = normalizePreferenceCompareValue(subscriptionPreference[field]);
      return value.length > 0 && value !== "all";
    });

    const hasExactMatch =
      !hasActivePreferences ||
      feedItems.some((item) => priorityOrder.every((field) => isPreferenceMatched(item, subscriptionPreference, field)));

    let bestMatchHint: string | undefined;
    if (hasActivePreferences && !hasExactMatch) {
      const bestMatch = findBestPreferenceMatch(feedItems, subscriptionPreference);
      if (bestMatch) {
        const subgroupText = resolvePreferenceDisplayValue(
          bestMatch.subgroup?.trim() || "all",
          noPreferenceLabel,
          noSubtitleLabel
        );
        const resolutionText = resolvePreferenceDisplayValue(
          bestMatch.resolution?.trim() || "all",
          noPreferenceLabel,
          noSubtitleLabel
        );
        const subtitleText = resolvePreferenceDisplayValue(
          normalizeSubtitleFilterValue(bestMatch.subtitleType),
          noPreferenceLabel,
          noSubtitleLabel
        );
        bestMatchHint =
          language === "zh"
            ? `当前最接近偏好的片源：${subgroupText} / ${resolutionText} / ${subtitleText}`
            : `Closest available source: ${subgroupText} / ${resolutionText} / ${subtitleText}`;
      }
    }

    setConfirmDialog({
      type: "subscribe",
      title: anime.ch_title || anime.en_title || anime.jp_title || "Unknown Title",
      subgroup: subscribePreferencePreview.subgroup,
      resolution: subscribePreferencePreview.resolution,
      subtitleType: subscribePreferencePreview.subtitleType,
      bestMatchHint,
    });
  }, [
    anime.bangumi_id,
    anime.ch_title,
    anime.en_title,
    anime.jp_title,
    isSubscribed,
    isSubscribeBusy,
    language,
    feedItems,
    noPreferenceLabel,
    noSubtitleLabel,
    subscriptionPreference,
    subscribeMikanBangumiId,
    subscribePreferencePreview.resolution,
    subscribePreferencePreview.subgroup,
    subscribePreferencePreview.subtitleType,
  ]);

  const closeConfirmDialog = useCallback(() => {
    if (confirmBusy) {
      return;
    }
    setConfirmDialog(null);
  }, [confirmBusy]);

  const confirmDialogAction = useCallback(async () => {
    if (!confirmDialog || confirmBusy || confirmDialog.type === "unsubscribe") {
      return;
    }

    setConfirmBusy(true);
    try {
      if (confirmDialog.type === "download") {
        await executeDownload(confirmDialog.item);
      } else if (confirmDialog.type === "remove") {
        await executeRemove(confirmDialog.hash);
      } else {
        await executeSubscribe();
      }
      setConfirmDialog(null);
    } finally {
      setConfirmBusy(false);
    }
  }, [confirmBusy, confirmDialog, executeDownload, executeRemove, executeSubscribe]);

  const confirmUnsubscribeAction = useCallback(async (action: CancelSubscriptionAction) => {
    if (!confirmDialog || confirmDialog.type !== "unsubscribe" || confirmBusy) {
      return;
    }

    setConfirmBusy(true);
    try {
      await executeUnsubscribe(action);
      setConfirmDialog(null);
    } finally {
      setConfirmBusy(false);
    }
  }, [confirmBusy, confirmDialog, executeUnsubscribe]);

  useEffect(() => {
    if (!open) {
      return;
    }
    void loadSeasons();
  }, [open, anime, loadSeasons]);

  useEffect(() => {
    if (seasons.length > 0) {
      void loadFeed();
    }
  }, [seasons, selectedSeasonIndex, loadFeed]);

  useEffect(() => {
    if (!open) {
      setConfirmDialog(null);
      setConfirmBusy(false);
      setIsSubscribed(false);
      setCurrentSubscriptionId(null);
      setIsSubscribeBusy(false);
      setSubscriptionPreference(DEFAULT_SUBSCRIPTION_PREFERENCE);
    }
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    let cancelled = false;
    void settingsApi
      .getSettingsProfile()
      .then((settingsProfile) => {
        if (cancelled) {
          return;
        }

        const incomingOrder = settingsProfile.downloadPreferences.priorityOrder;
        const normalizedOrder =
          incomingOrder.length === 3 &&
          DEFAULT_SUBSCRIPTION_PREFERENCE.priorityOrder.every((field) => incomingOrder.includes(field))
            ? incomingOrder
            : DEFAULT_SUBSCRIPTION_PREFERENCE.priorityOrder;

        setSubscriptionPreference({
          subgroup: settingsProfile.downloadPreferences.subgroup || DEFAULT_SUBSCRIPTION_PREFERENCE.subgroup,
          resolution: settingsProfile.downloadPreferences.resolution || DEFAULT_SUBSCRIPTION_PREFERENCE.resolution,
          subtitleType: settingsProfile.downloadPreferences.subtitleType || DEFAULT_SUBSCRIPTION_PREFERENCE.subtitleType,
          priorityOrder: normalizedOrder,
        });
      })
      .catch(() => {
        if (!cancelled) {
          setSubscriptionPreference(DEFAULT_SUBSCRIPTION_PREFERENCE);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    const bangumiId = Number.parseInt(anime.bangumi_id, 10);
    if (!Number.isFinite(bangumiId) || bangumiId <= 0) {
      setIsSubscribed(false);
      setCurrentSubscriptionId(null);
      return;
    }

    let cancelled = false;
    setIsSubscribeBusy(true);

    void subscriptionApi
      .getSubscriptionByBangumiId(bangumiId)
      .then((subscription) => {
        if (cancelled) {
          return;
        }
        if (!subscription) {
          setIsSubscribed(false);
          setCurrentSubscriptionId(null);
          return;
        }
        setIsSubscribed(Boolean(subscription.isEnabled));
        setCurrentSubscriptionId(subscription.id);
      })
      .catch(() => {
        if (cancelled) {
          return;
        }
        setIsSubscribed(false);
        setCurrentSubscriptionId(null);
      })
      .finally(() => {
        if (!cancelled) {
          setIsSubscribeBusy(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [open, anime.bangumi_id]);

  useEffect(() => {
    return () => {
      stopPollingRef.current?.();
      stopPollingRef.current = null;
    };
  }, [open]);

  useEffect(() => {
    const handleEsc = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };

    if (open) {
      document.addEventListener("keydown", handleEsc);
      document.body.style.overflow = "hidden";
    }

    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "unset";
    };
  }, [open, onClose]);

  if (!open) return null;

  const latestText =
    latestEpisode !== null
      ? `EP ${latestEpisode}`
      : latestPublishedAt
        ? new Date(latestPublishedAt).toLocaleString()
        : null;
  const confirmDialogTitle = !confirmDialog
    ? ""
    : confirmDialog.type === "download"
      ? confirmDownloadTitle
      : confirmDialog.type === "remove"
        ? confirmRemoveTitle
        : confirmDialog.type === "subscribe"
          ? confirmSubscribeTitle
          : confirmUnsubscribeTitle;
  const confirmDialogMessage = !confirmDialog
    ? ""
    : confirmDialog.type === "download"
      ? (
        language === "zh"
          ? `\u786e\u8ba4\u540eqBittorrent\u5c06\u4f1a\u7acb\u5373\u5f00\u59cb\u300c${confirmDialog.item.title}\u300d\u3002`
          : `Confirm to start "${confirmDialog.item.title}" in qBittorrent immediately.`
      )
      : confirmDialog.type === "remove"
        ? (
        language === "zh"
          ? `\u786e\u8ba4\u540eqBittorrent\u5c06\u4f1a\u7acb\u5373\u5220\u9664\u300c${confirmDialog.title}\u300d\u4efb\u52a1\uff08\u5305\u62ec\u6587\u4ef6\uff09\u3002`
          : `Confirm to delete "${confirmDialog.title}" from qBittorrent immediately (including files).`
      )
        : confirmDialog.type === "subscribe"
          ? (
            language === "zh"
              ? `\u786e\u8ba4\u8ba2\u9605${confirmDialog.title}\uff1f\u8ba2\u9605\u540e\u5c06\u6309\u7167\u60a8\u5728\u8bbe\u7f6e\u4e2d\u7684\u9884\u8bbe\u7247\u6e90\u504f\u597d\u8fdb\u884c\u4e0b\u8f7d\u3002\u5f53\u524d\u504f\u597d\u4e3a\uff1a${confirmDialog.subgroup}\uff0c${confirmDialog.resolution}\uff0c${confirmDialog.subtitleType}\u3002\u82e5\u65e0\u6700\u4f73\u5339\u914d\uff0c\u5219\u6309\u504f\u597d\u987a\u5e8f\u8fdb\u884c\u5339\u914d\u4e0b\u8f7d\u3002\u8fd9\u4e9b\u529f\u80fd\u76ee\u524d\u4ecd\u5728\u5b8c\u5584\u4e2d\uff0c\u540e\u7eed\u4f1a\u5728\u8bbe\u7f6e\u9875\u63d0\u4f9b\u5b8c\u6574\u914d\u7f6e\u3002${confirmDialog.bestMatchHint ? `\n${confirmDialog.bestMatchHint}` : ""}`
              : `Confirm subscription for "${confirmDialog.title}"? Downloads will follow your preset source preferences in Settings. Current preference: ${confirmDialog.subgroup}, ${confirmDialog.resolution}, ${confirmDialog.subtitleType}. If no best match is found, fallback matching will follow your preference order. This flow is still being completed and will be fully configurable in Settings.${confirmDialog.bestMatchHint ? `\n${confirmDialog.bestMatchHint}` : ""}`
          )
          : (
            language === "zh"
              ? "\u786e\u8ba4\u53d6\u6d88\u8ba2\u9605\u5417\uff1f\u60a8\u5e0c\u671b\u5982\u4f55\u5904\u7406\u5df2\u4e0b\u8f7d\u7684\u52a8\u6f2b\u6587\u4ef6\uff1f"
              : "Confirm unsubscribe? How would you like to handle downloaded anime files?"
      );
  const isUnsubscribeDialog = confirmDialog?.type === "unsubscribe";
  const subscribeButtonLabel = isSubscribed ? unsubscribeLabel : subscribeLabel;

  const modalContent = (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" onClick={onClose}>
      <div className="absolute inset-0 bg-black/50" />

        <div
          className="relative z-10 w-full max-w-3xl bg-white rounded-lg shadow-2xl overflow-hidden animate-fadeIn"
          onClick={(event) => event.stopPropagation()}
        >
        <div className="absolute flex items-center gap-1" style={{ top: "1rem", right: "1rem", zIndex: 20 }}>
          <div className="flex-shrink-0 inline-flex items-center px-2 py-0.5 bg-yellow-100 rounded-full">
            <StarIcon />
            <span className="text-sm font-semibold text-gray-900">{anime.score || "N/A"}</span>
          </div>

          <button
            type="button"
            onClick={handleSubscriptionButtonClick}
            className="group group/subscribe"
            style={{ all: "unset", padding: "0", cursor: isSubscribeBusy || confirmBusy ? "not-allowed" : "pointer" }}
            aria-label={subscribeButtonLabel}
            title={subscribeButtonLabel}
            disabled={isSubscribeBusy || confirmBusy}
          >
            <span
              className={`relative inline-flex h-8 w-8 items-center justify-center gap-0 overflow-hidden rounded-md bg-transparent text-gray-600 transition-all duration-200 ${subscribeExpandWidthClass} group-hover/subscribe:gap-1`}
            >
              {!isSubscribed && (
                <BellIcon className="h-5 w-5 shrink-0 transition-colors duration-200 group-hover/subscribe:text-black" />
              )}
              {isSubscribed && (
                <>
                  <CheckIcon className="h-5 w-5 shrink-0 text-green-600 transition-opacity duration-200 group-hover/subscribe:opacity-0" />
                  <span className="absolute inset-0 inline-flex items-center justify-center opacity-0 transition-opacity duration-200 group-hover/subscribe:opacity-100">
                    <CloseIcon className="h-5 w-5 shrink-0" />
                  </span>
                </>
              )}
              <span
                className={`max-w-0 overflow-hidden whitespace-nowrap text-xs font-bold opacity-0 transition-all duration-200 ${subscribeTextExpandClass} group-hover/subscribe:opacity-100`}
              >
                {isSubscribed ? unsubscribeLabel : subscribeLabel}
              </span>
            </span>
          </button>

          <button
            onClick={onClose}
            className="group"
            style={{ all: "unset", padding: "0", cursor: "pointer" }}
            aria-label="Close"
          >
            <CloseIcon />
          </button>
        </div>

        <div className="flex flex-col max-h-[90vh]">
          <div className="flex pt-4 px-6 pb-6 gap-6">
            <div className="w-48 flex-shrink-0">
              <img
                src={anime.images?.portrait || ""}
                alt={primaryTitle}
                className="w-full h-auto rounded-lg object-cover shadow-md"
              />
            </div>

            <div className="flex-1 flex flex-col min-w-0">
              <div className="mb-4">
                <h2
                  className="mb-1 text-2xl font-bold text-gray-900 break-words [overflow-wrap:anywhere]"
                  style={{ maxWidth: "calc(100% - 12rem)" }}
                >
                  {primaryTitle}
                </h2>
                {secondaryTitle && (
                  <p
                    className="text-sm text-gray-500 break-words [overflow-wrap:anywhere]"
                    style={{ maxWidth: "calc(100% - 12rem)" }}
                  >
                    {secondaryTitle}
                  </p>
                )}
              </div>

              <div className="mb-4 flex-1 overflow-y-auto">
                <h3 className="text-sm font-semibold text-gray-700 mb-2">{descriptionLabel}</h3>
                <p className="text-gray-600 text-sm leading-relaxed whitespace-pre-wrap max-h-32 overflow-y-auto">
                  {description}
                </p>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 mb-2">{linksLabel}</h3>
                <div className="flex flex-wrap gap-2">
                  {anime.external_urls?.bangumi && (
                    <a
                      href={anime.external_urls.bangumi}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center px-3 py-1.5 bg-transparent border border-gray-300 hover:bg-gray-100 text-gray-700 rounded-lg transition-colors text-sm"
                    >
                      Bangumi
                      <ExternalLinkIcon />
                    </a>
                  )}
                  {anime.external_urls?.tmdb && (
                    <a
                      href={anime.external_urls.tmdb}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center px-3 py-1.5 bg-transparent border border-gray-300 hover:bg-gray-100 text-gray-700 rounded-lg transition-colors text-sm"
                    >
                      TMDB
                      <ExternalLinkIcon />
                    </a>
                  )}
                  {anime.external_urls?.anilist && (
                    <a
                      href={anime.external_urls.anilist}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center px-3 py-1.5 bg-transparent border border-gray-300 hover:bg-gray-100 text-gray-700 rounded-lg transition-colors text-sm"
                    >
                      AniList
                      <ExternalLinkIcon />
                    </a>
                  )}
                </div>
              </div>
            </div>
          </div>

          <div className="border-t border-gray-200 p-6 bg-gray-50">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-bold text-gray-900">{downloadLabel}</h3>
              {latestText && (
                <p className="text-sm text-gray-600">
                  {latestEpisodeLabel}: <span className="font-semibold text-gray-800">{latestText}</span>
                </p>
              )}
            </div>

            {loading && <LoadingSpinner />}
            {error && <ErrorMessage message={error} />}

            {!loading && !error && shouldShowSeasonSelector && (
              <div className="mb-4">
                <label className="text-sm font-medium text-gray-700">{seasonLabel}</label>
                <select
                  value={selectedSeasonIndex}
                  onChange={handleSeasonChange}
                  className="mt-1 block w-full rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  {seasons.map((season, index) => (
                    <option key={`${season.mikanBangumiId}-${index}`} value={index}>
                      {season.seasonName} {season.year ? `(${season.year})` : ""}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {!loading && !error && feedItems.length > 0 && (
              <div className="flex gap-3 mb-4 flex-wrap">
                <select
                  value={taskFilter}
                  onChange={(event) => setTaskFilter(event.target.value as DownloadTaskFilter)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{statusLabel}: {allLabel}</option>
                  <option value="downloading">{statusLabel}: {statusDownloadingLabel}</option>
                  <option value="completed">{statusLabel}: {statusCompletedLabel}</option>
                </select>

                <select
                  value={downloadPreferences.resolution}
                  onChange={(event) => handlePreferenceChange("resolution", event.target.value)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{resolutionLabel}: {allLabel}</option>
                  {resolutionOptions.map((resolution) => (
                    <option key={resolution} value={resolution}>
                      {resolution}
                    </option>
                  ))}
                </select>

                <select
                  value={downloadPreferences.subgroup}
                  onChange={(event) => handlePreferenceChange("subgroup", event.target.value)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{subgroupLabel}: {allLabel}</option>
                  {subgroupOptions.map((subgroup) => (
                    <option key={subgroup} value={subgroup}>
                      {subgroup}
                    </option>
                  ))}
                </select>

                <select
                  value={downloadPreferences.subtitleType}
                  onChange={(event) => handlePreferenceChange("subtitleType", event.target.value)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{subtitleLabel}: {allLabel}</option>
                  {subtitleTypeOptions.map((subtitleType) => (
                    <option key={subtitleType} value={subtitleType}>
                      {subtitleType === NO_SUBTITLE_FILTER_VALUE ? noSubtitleLabel : subtitleType}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {!loading && !error && filteredItems.length > 0 && (
              <div className="space-y-3 max-h-72 overflow-y-auto pr-1">
                {episodeGroups.map((group) => {
                  const groupKey = getEpisodeGroupKey(group);
                  return (
                    <DownloadEpisodeGroup
                      key={groupKey}
                      language={language}
                      groupKey={groupKey}
                      episode={group.episode}
                      isCollectionGroup={group.type === "collection"}
                      collapsed={collapsedGroupKeys.has(groupKey)}
                      onToggleCollapse={toggleGroupCollapse}
                      items={group.items}
                      downloadStatus={downloadStatus}
                      stateOverrides={buttonStateOverrides}
                      torrentInfo={torrentInfo}
                      busyHash={busyHash}
                      onDownload={requestDownload}
                      onPause={handlePause}
                      onResume={handleResume}
                      onRemove={requestRemove}
                    />
                  );
                })}
              </div>
            )}

            {!loading && !error && filteredItems.length === 0 && feedItems.length === 0 && (
              <p className="text-gray-500">{noDownloadLabel}</p>
            )}

            {!loading && !error && filteredItems.length === 0 && feedItems.length > 0 && (
              <p className="text-gray-500">
                {language === "zh" ? "\u6ca1\u6709\u7b26\u5408\u6761\u4ef6\u7684\u4e0b\u8f7d\u6e90" : "No matching download sources"}
              </p>
            )}
          </div>
        </div>
      </div>

      {confirmDialog && (
        <div
          className="absolute inset-0 z-30 flex items-center justify-center bg-black/35 p-4"
          onClick={(event) => {
            event.stopPropagation();
            closeConfirmDialog();
          }}
        >
          <div
            className="w-full max-w-sm rounded-xl border border-gray-200 bg-white p-5 shadow-2xl"
            onClick={(event) => event.stopPropagation()}
          >
            <h4 className="text-lg font-semibold text-gray-900">{confirmDialogTitle}</h4>
            <p className="mt-2 text-base text-gray-600">{confirmDialogMessage}</p>

            {!isUnsubscribeDialog && (
              <div className="mt-5 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={closeConfirmDialog}
                  disabled={confirmBusy}
                  className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs text-gray-900 transition-colors hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {cancelLabel}
                </button>
                <button
                  type="button"
                  onClick={() => void confirmDialogAction()}
                  disabled={confirmBusy}
                  className="rounded-md border border-gray-300 bg-gray-100 px-3 py-1.5 text-xs text-gray-900 transition-colors hover:bg-gray-200 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {confirmBusy ? (language === "zh" ? "\u5904\u7406\u4e2d..." : "Processing...") : confirmActionLabel}
                </button>
              </div>
            )}

            {isUnsubscribeDialog && (
              <div className="mt-5 flex flex-wrap items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={closeConfirmDialog}
                  disabled={confirmBusy}
                  className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs text-gray-900 transition-colors hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {rollbackLabel}
                </button>
                <button
                  type="button"
                  onClick={() => void confirmUnsubscribeAction("keep_files")}
                  disabled={confirmBusy}
                  className="rounded-md border border-gray-300 bg-gray-100 px-3 py-1.5 text-xs text-gray-900 transition-colors hover:bg-gray-200 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {confirmBusy ? (language === "zh" ? "\u5904\u7406\u4e2d..." : "Processing...") : cancelAndKeepLabel}
                </button>
                <button
                  type="button"
                  onClick={() => void confirmUnsubscribeAction("delete_files")}
                  disabled={confirmBusy}
                  className="rounded-md border border-red-300 bg-red-50 px-3 py-1.5 text-xs text-red-700 transition-colors hover:bg-red-100 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {confirmBusy ? (language === "zh" ? "\u5904\u7406\u4e2d..." : "Processing...") : cancelAndDeleteLabel}
                </button>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );

  return createPortal(modalContent, document.body);
}

export default AnimeDetailModal;
