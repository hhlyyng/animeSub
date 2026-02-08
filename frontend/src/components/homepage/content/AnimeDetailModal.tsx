import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ChangeEvent } from "react";
import { createPortal } from "react-dom";
import toast from "react-hot-toast";
import type { AnimeInfo } from "./AnimeInfoType";
import { useAppStore } from "../../../stores/useAppStores";
import type { MikanSeasonInfo, ParsedRssItem, TorrentInfo } from "../../../types/mikan";
import * as mikanApi from "../../../services/mikanApi";
import { StarIcon } from "../../icon/StarIcon";
import { CloseIcon } from "../../icon/CloseIcon";
import { ExternalLinkIcon } from "../../icon/ExternalLinkIcon";
import { DownloadEpisodeGroup } from "./DownloadEpisodeGroup";
import { LoadingSpinner } from "./LoadingSpinner";
import { ErrorMessage } from "./ErrorMessage";

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
  const stopPollingRef = useRef<(() => void) | null>(null);

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
  const subgroupLabel = language === "zh" ? "\u5b57\u5e55\u7ec4" : "Subgroup";
  const subtitleLabel = language === "zh" ? "\u5b57\u5e55" : "Subtitle";
  const allLabel = language === "zh" ? "\u5168\u90e8" : "All";

  const loadSeasons = useCallback(async () => {
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
      return;
    }

    const searchTitle = anime.ch_title || anime.en_title || anime.jp_title;
    if (!searchTitle) {
      setError(language === "zh" ? "\u6ca1\u6709\u53ef\u641c\u7d22\u7684\u6807\u9898" : "No title available for search");
      return;
    }

    try {
      setLoading(true);
      setError(null);

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
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to search";
      setError(errorMessage);
    } finally {
      setLoading(false);
    }
  }, [anime, explicitSeasonNumber, language]);

  const loadFeed = useCallback(async () => {
    if (seasons.length === 0) return;

    const season = seasons[selectedSeasonIndex];
    if (!season) return;

    try {
      setLoading(true);
      setError(null);

      const feed = await mikanApi.getMikanFeed(season.mikanBangumiId, anime.bangumi_id);
      setFeedItems(feed.items);
      setLatestEpisode(feed.latestEpisode ?? null);
      setLatestPublishedAt(feed.latestPublishedAt ?? null);

      // Keep cross-anime preference for resolution, reset scoped filters to avoid stale invalid options.
      setDownloadPreferences({ subgroup: "all", subtitleType: "all" });

      const status = await mikanApi.checkDownloadStatus(feed.items);
      setDownloadStatus(status);

      stopPollingRef.current?.();
      stopPollingRef.current = mikanApi.startProgressPolling(5000, (progresses: Map<string, TorrentInfo>) => {
        setTorrentInfo(progresses);
      });
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to load feed";
      setError(errorMessage);
    } finally {
      setLoading(false);
    }
  }, [anime.bangumi_id, seasons, selectedSeasonIndex, setDownloadPreferences]);

  const resolutionOptions = useMemo(() => uniqueSorted(feedItems.map((item) => item.resolution)), [feedItems]);

  const subgroupOptions = useMemo(() => {
    const candidateItems = feedItems.filter((item) => {
      if (downloadPreferences.resolution !== "all" && item.resolution !== downloadPreferences.resolution) {
        return false;
      }
      if (downloadPreferences.subtitleType !== "all" && item.subtitleType !== downloadPreferences.subtitleType) {
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
    return uniqueSorted(candidateItems.map((item) => item.subtitleType));
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

  const filteredItems = useMemo(
    () =>
      feedItems.filter((item) => {
        if (downloadPreferences.resolution !== "all" && item.resolution !== downloadPreferences.resolution) {
          return false;
        }
        if (downloadPreferences.subgroup !== "all" && item.subgroup !== downloadPreferences.subgroup) {
          return false;
        }
        if (downloadPreferences.subtitleType !== "all" && item.subtitleType !== downloadPreferences.subtitleType) {
          return false;
        }
        return true;
      }),
    [feedItems, downloadPreferences]
  );

  const episodeGroups = useMemo(() => groupItemsByEpisode(filteredItems), [filteredItems]);

  const markTorrentState = useCallback((hash: string, patch: Partial<TorrentInfo>) => {
    setTorrentInfo((prev) => {
      const next = new Map(prev);
      const existing = next.get(hash);
      if (!existing) return prev;
      next.set(hash, { ...existing, ...patch });
      return next;
    });
  }, []);

  const handleDownload = useCallback(
    async (item: ParsedRssItem) => {
      if (item.canDownload === false) {
        toast.error(language === "zh" ? "\u8d44\u6e90 hash \u7f3a\u5931\uff0c\u65e0\u6cd5\u4e0b\u8f7d" : "Torrent hash is missing, cannot download");
        return;
      }

      try {
        const requestHash = item.torrentHash?.trim().toUpperCase() ?? "";
        if (requestHash.length > 0) {
          setBusyHash(requestHash);
        }
        setDownloadLoading(true);

        const qbHash = await mikanApi.downloadTorrent({
          magnetLink: item.magnetLink,
          torrentUrl: item.torrentUrl,
          title: item.title,
          torrentHash: requestHash,
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

        toast.success(language === "zh" ? "\u4e0b\u8f7d\u6210\u529f" : "Downloaded successfully");
      } catch (err) {
        const errorMsg = err instanceof Error ? err.message : "Failed to download";
        toast.error(language === "zh" ? `\u4e0b\u8f7d\u5931\u8d25: ${errorMsg}` : `Download failed: ${errorMsg}`);
      } finally {
        setBusyHash(null);
        setDownloadLoading(false);
      }
    },
    [language, setDownloadLoading]
  );

  const handlePause = useCallback(async (hash: string) => {
    try {
      setBusyHash(hash);
      await mikanApi.pauseTorrent(hash);
      markTorrentState(hash, { state: "pausedDL" });
      toast.success(language === "zh" ? "\u5df2\u6682\u505c\u4e0b\u8f7d" : "Download paused");
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : "Failed to pause torrent";
      toast.error(language === "zh" ? `\u6682\u505c\u5931\u8d25: ${errorMsg}` : `Pause failed: ${errorMsg}`);
    } finally {
      setBusyHash(null);
    }
  }, [language, markTorrentState]);

  const handleResume = useCallback(async (hash: string) => {
    try {
      setBusyHash(hash);
      await mikanApi.resumeTorrent(hash);
      markTorrentState(hash, { state: "downloading" });
      toast.success(language === "zh" ? "\u5df2\u7ee7\u7eed\u4e0b\u8f7d" : "Download resumed");
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : "Failed to resume torrent";
      toast.error(language === "zh" ? `\u7ee7\u7eed\u5931\u8d25: ${errorMsg}` : `Resume failed: ${errorMsg}`);
    } finally {
      setBusyHash(null);
    }
  }, [language, markTorrentState]);

  const handleRemove = useCallback(async (hash: string) => {
    try {
      setBusyHash(hash);
      await mikanApi.removeTorrent(hash, false);

      setTorrentInfo((prev) => {
        const next = new Map(prev);
        next.delete(hash);
        return next;
      });

      setDownloadStatus((prev) => {
        const next = new Map(prev);
        next.set(hash, false);
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

  const handleSeasonChange = (event: ChangeEvent<HTMLSelectElement>) => {
    setSelectedSeasonIndex(Number.parseInt(event.target.value, 10));
  };

  const handlePreferenceChange = (key: keyof typeof downloadPreferences, value: string) => {
    setDownloadPreferences({ [key]: value });
  };

  useEffect(() => {
    if (open && anime.jp_title) {
      void loadSeasons();
    }
  }, [open, anime, loadSeasons]);

  useEffect(() => {
    if (seasons.length > 0) {
      void loadFeed();
    }
  }, [seasons, selectedSeasonIndex, loadFeed]);

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

  const modalContent = (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" onClick={onClose}>
      <div className="absolute inset-0 bg-black/50" />

      <div
        className="relative z-10 w-full max-w-3xl bg-white rounded-lg shadow-2xl overflow-hidden animate-fadeIn"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="absolute flex items-center gap-3" style={{ top: "1rem", right: "1rem", zIndex: 20 }}>
          <div className="flex-shrink-0 inline-flex items-center px-2.5 py-0.5 bg-yellow-100 rounded-full">
            <StarIcon />
            <span className="text-sm font-semibold text-gray-900">{anime.score || "N/A"}</span>
          </div>

          <button
            onClick={onClose}
            className="group"
            style={{ all: "unset", padding: "0.25rem", cursor: "pointer" }}
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
              <div className="mb-4 pr-24">
                <h2 className="text-2xl font-bold text-gray-900 mb-1">{primaryTitle}</h2>
                {secondaryTitle && <p className="text-sm text-gray-500">{secondaryTitle}</p>}
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

            {!loading && !error && filteredItems.length > 0 && (
              <div className="flex gap-3 mb-4 flex-wrap">
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
                      {subtitleType}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {!loading && !error && filteredItems.length > 0 && (
              <div className="space-y-3 max-h-72 overflow-y-auto pr-1">
                {episodeGroups.map((group) => (
                  <DownloadEpisodeGroup
                    key={
                      group.type === "collection"
                        ? "ep-collection"
                        : group.episode === null
                          ? "ep-unknown"
                          : `ep-${group.episode}`
                    }
                    episode={group.episode}
                    isCollectionGroup={group.type === "collection"}
                    items={group.items}
                    downloadStatus={downloadStatus}
                    torrentInfo={torrentInfo}
                    busyHash={busyHash}
                    onDownload={handleDownload}
                    onPause={handlePause}
                    onResume={handleResume}
                    onRemove={handleRemove}
                  />
                ))}
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
    </div>
  );

  return createPortal(modalContent, document.body);
}

export default AnimeDetailModal;
