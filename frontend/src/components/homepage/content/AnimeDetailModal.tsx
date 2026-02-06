import { useEffect, useState, useCallback } from "react";
import { createPortal } from "react-dom";
import toast from 'react-hot-toast';
import type { AnimeInfo } from "./AnimeInfoType";
import { useAppStore } from "../../../stores/useAppStores";
import { DownloadItem } from "./DownloadItem";
import { LoadingSpinner } from "./LoadingSpinner";
import { ErrorMessage } from "./ErrorMessage";
import type { MikanSeasonInfo, ParsedRssItem } from "../../../types/mikan";
import * as mikanApi from "../../../services/mikanApi";

type AnimeDetailModalProps = {
  anime: AnimeInfo;
  open: boolean;
  onClose: () => void;
};

export function AnimeDetailModal({ anime, open, onClose }: AnimeDetailModalProps) {
  const language = useAppStore((state) => state.language);
  const downloadPreferences = useAppStore((state) => state.downloadPreferences);
  const setDownloadPreferences = useAppStore((state) => state.setDownloadPreferences);
  const setDownloadLoading = useAppStore((state) => state.setDownloadLoading);

  // Download state
  const [seasons, setSeasons] = useState<MikanSeasonInfo[]>([]);
  const [selectedSeasonIndex, setSelectedSeasonIndex] = useState(0);
  const [feedItems, setFeedItems] = useState<ParsedRssItem[]>([]);
  const [availableSubgroups, setAvailableSubgroups] = useState<string[]>(['all']);
  const [availableResolutions, setAvailableResolutions] = useState<string[]>(['all']);
  const [availableSubtitleTypes, setAvailableSubtitleTypes] = useState<string[]>(['all']);
  const [downloadStatus, setDownloadStatus] = useState<Map<string, boolean>>(new Map());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [downloadingItem, setDownloadingItem] = useState<string | null>(null);

  // 根据语言选择标题和描述，fallback to jp_title if both ch/en are empty
  const primaryTitle = language === 'zh'
    ? (anime.ch_title || anime.en_title || anime.jp_title)
    : (anime.en_title || anime.ch_title || anime.jp_title);

  // Only show secondary title if it's different from primary
  const secondaryTitle = anime.jp_title !== primaryTitle ? anime.jp_title : null;

  // Check for non-empty descriptions (handle empty strings)
  const hasChDesc = anime.ch_desc && anime.ch_desc.trim().length > 0;
  const hasEnDesc = anime.en_desc && anime.en_desc.trim().length > 0;

  const description = language === 'zh'
    ? (hasChDesc ? anime.ch_desc : (hasEnDesc ? anime.en_desc : '暂无描述'))
    : (hasEnDesc ? anime.en_desc : (hasChDesc ? anime.ch_desc : 'No description available'));

  const descriptionLabel = language === 'zh' ? '简介' : 'Synopsis';
  const linksLabel = language === 'zh' ? '相关链接' : 'Links';
  const downloadLabel = language === 'zh' ? '下载源' : 'Download Sources';
  const noDownloadLabel = language === 'zh' ? '暂无可用下载源' : 'No download sources available';
  const seasonLabel = language === 'zh' ? '季度' : 'Season';
  const resolutionLabel = language === 'zh' ? '分辨率' : 'Resolution';
  const subgroupLabel = language === 'zh' ? '字幕组' : 'Subgroup';
  const subtitleLabel = language === 'zh' ? '字幕' : 'Subtitle';
  const allLabel = language === 'zh' ? '全部' : 'All';

  // Load seasons when modal opens
  useEffect(() => {
    if (open && anime.jp_title) {
      console.log('Modal opened, anime:', anime);
      loadSeasons();
    }
  }, [open, anime, loadSeasons]);

  // Load feed when season changes
  useEffect(() => {
    if (seasons.length > 0) {
      loadFeed();
    }
  }, [selectedSeasonIndex, seasons, loadFeed]);

  const loadSeasons = useCallback(async () => {
    console.log('Loading seasons for anime:', anime);
    console.log('jp_title:', anime.jp_title);
    console.log('mikan_bangumi_id:', anime.mikan_bangumi_id);

    // If we already have MikanBangumiId from database, use it directly
    if (anime.mikan_bangumi_id) {
      console.log('Using cached Mikan Bangumi ID:', anime.mikan_bangumi_id);

      try {
        setLoading(true);
        setError(null);

        const season = {
          seasonName: "Season 1",
          mikanBangumiId: anime.mikan_bangumi_id,
          year: 0
        };

        setSeasons([season]);
        setSelectedSeasonIndex(0);
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to load seasons';
        setError(errorMessage);
        console.error('Error loading seasons:', err);
      } finally {
        setLoading(false);
      }
      return;
    }

    if (!anime.jp_title) {
      console.error('No jp_title available');
      setError('No anime title available');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      const result = await mikanApi.searchMikanAnime(anime.jp_title);
      console.log('Search result:', result);
      setSeasons(result.seasons);
      setSelectedSeasonIndex(result.defaultSeason);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to search';
      setError(errorMessage);
      console.error('Error searching seasons:', err);
    } finally {
      setLoading(false);
    }
  }, [anime]);

  const loadFeed = useCallback(async () => {
    if (seasons.length === 0) return;

    try {
      setLoading(true);
      setError(null);
      const season = seasons[selectedSeasonIndex];
      const feed = await mikanApi.getMikanFeed(season.mikanBangumiId);

      setFeedItems(feed.items);
      setAvailableSubgroups(['all', ...feed.availableSubgroups]);
      setAvailableResolutions(['all', ...feed.availableResolutions]);
      setAvailableSubtitleTypes(['all', ...feed.availableSubtitleTypes]);

      // Check download status
      const status = await mikanApi.checkDownloadStatus(feed.items);
      setDownloadStatus(status);

      // Cache in sessionStorage
      const cacheKey = `mikan-feed-${anime.jp_title}-${season.mikanBangumiId}`;
      try {
        sessionStorage.setItem(cacheKey, JSON.stringify(feed));
      } catch (e) {
        console.warn('Failed to cache feed:', e);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load feed');
      console.error('Error loading feed:', err);
    } finally {
      setLoading(false);
    }
  }, [seasons, selectedSeasonIndex, anime]);

  const handleDownload = async (item: ParsedRssItem) => {
    try {
      setDownloadingItem(item.torrentHash);
      setDownloadLoading(true);

      await mikanApi.downloadTorrent({
        magnetLink: item.magnetLink,
        torrentUrl: item.torrentUrl,
        title: item.title,
        torrentHash: item.torrentHash
      });

      // Update download status
      const newStatus = new Map(downloadStatus);
      newStatus.set(item.torrentHash, true);
      setDownloadStatus(newStatus);

      toast.success(language === 'zh' ? '下载成功' : 'Downloaded successfully');
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to download';
      toast.error(language === 'zh' ? `下载失败: ${errorMsg}` : `Download failed: ${errorMsg}`);
      console.error('Error downloading:', err);
    } finally {
      setDownloadingItem(null);
      setDownloadLoading(false);
    }
  };

  const handleCopyMagnet = async (magnetLink: string) => {
    try {
      await navigator.clipboard.writeText(magnetLink);
      toast.success(language === 'zh' ? '磁力链接已复制' : 'Magnet link copied');
    } catch (err) {
      toast.error(language === 'zh' ? '复制失败' : 'Failed to copy');
      console.error('Error copying:', err);
    }
  };

  const handleSeasonChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setSelectedSeasonIndex(parseInt(e.target.value));
  };

  const handlePreferenceChange = (key: keyof typeof downloadPreferences, value: string) => {
    setDownloadPreferences({ [key]: value });
  };

  // Filter items based on preferences
  const filteredItems = feedItems.filter(item => {
    if (downloadPreferences.resolution !== 'all' && item.resolution !== downloadPreferences.resolution) {
      return false;
    }
    if (downloadPreferences.subgroup !== 'all' && item.subgroup !== downloadPreferences.subgroup) {
      return false;
    }
    if (downloadPreferences.subtitleType !== 'all' && item.subtitleType !== downloadPreferences.subtitleType) {
      return false;
    }
    return true;
  });

  // 按 ESC 键关闭
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
      }
    };

    if (open) {
      document.addEventListener("keydown", handleEsc);
      // 禁止背景滚动
      document.body.style.overflow = "hidden";
    }

    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "unset";
    };
  }, [open, onClose]);

  if (!open) return null;

  const modalContent = (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      onClick={onClose}
    >
      {/* 背景遮罩 - 无 blur */}
      <div className="absolute inset-0 bg-black/50" />

      {/* Modal 内容 */}
      <div
        className="relative z-10 w-full max-w-3xl bg-white rounded-lg shadow-2xl overflow-hidden animate-fadeIn"
        onClick={(e) => e.stopPropagation()}
      >
        {/* 右上角固定区域：分数 + 关闭按钮 */}
        <div
          className="absolute flex items-center gap-3"
          style={{ top: '1rem', right: '1rem', zIndex: 20 }}
        >
          {/* 分数 */}
          <div className="flex-shrink-0 inline-flex items-center px-2.5 py-0.5 bg-yellow-100 rounded-full">
            <svg
              className="w-4 h-4 text-yellow-500 mr-1"
              fill="currentColor"
              viewBox="0 0 20 20"
            >
              <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
            </svg>
            <span className="text-sm font-semibold text-gray-900">
              {anime.score}
            </span>
          </div>

          {/* 关闭按钮 */}
          <button
            onClick={onClose}
            className="group"
            style={{
              all: 'unset',
              padding: '0.25rem',
              cursor: 'pointer',
            }}
            aria-label="Close"
          >
            <svg
              className="w-5 h-5"
              fill="none"
              viewBox="0 0 24 24"
            >
              <path
                className="stroke-gray-600 group-hover:stroke-black transition-colors"
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={4}
                d="M6 18L18 6M6 6l12 12"
              />
            </svg>
          </button>
        </div>

        <div className="flex flex-col max-h-[90vh]">
          {/* 上半部分: 信息区 */}
          <div className="flex pt-4 px-6 pb-6 gap-6">
            {/* 左: 图片 */}
            <div className="w-48 flex-shrink-0">
              <img
                src={anime.images.portrait}
                alt={primaryTitle}
                className="w-full h-auto rounded-lg object-cover shadow-md"
              />
            </div>

            {/* 右: 标题+描述+链接 */}
            <div className="flex-1 flex flex-col min-w-0">
              {/* 标题 */}
              <div className="mb-4 pr-24">
                <h2 className="text-2xl font-bold text-gray-900 mb-1">
                  {primaryTitle}
                </h2>
                {secondaryTitle && (
                  <p className="text-sm text-gray-500">{secondaryTitle}</p>
                )}
              </div>

              {/* 描述 */}
              <div className="mb-4 flex-1 overflow-y-auto">
                <h3 className="text-sm font-semibold text-gray-700 mb-2">{descriptionLabel}</h3>
                <p className="text-gray-600 text-sm leading-relaxed whitespace-pre-wrap max-h-32 overflow-y-auto">
                  {description}
                </p>
              </div>

              {/* 外部链接 */}
              <div>
                <h3 className="text-sm font-semibold text-gray-700 mb-2">{linksLabel}</h3>
                <div className="flex flex-wrap gap-2">
                  {anime.external_urls.bangumi && (
                    <a
                      href={anime.external_urls.bangumi}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center px-3 py-1.5 bg-transparent border border-gray-300 hover:bg-gray-100 text-gray-700 rounded-lg transition-colors text-sm"
                    >
                      Bangumi
                      <svg
                        className="w-3.5 h-3.5 ml-1.5"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                      >
                        <path
                          strokeLinecap="round"
                          strokeLinejoin="round"
                          strokeWidth={2}
                          d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
                        />
                      </svg>
                    </a>
                  )}
                  {anime.external_urls.tmdb && (
                    <a
                      href={anime.external_urls.tmdb}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center px-3 py-1.5 bg-transparent border border-gray-300 hover:bg-gray-100 text-gray-700 rounded-lg transition-colors text-sm"
                    >
                      TMDB
                      <svg
                        className="w-3.5 h-3.5 ml-1.5"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                      >
                        <path
                          strokeLinecap="round"
                          strokeLinejoin="round"
                          strokeWidth={2}
                          d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
                        />
                      </svg>
                    </a>
                  )}
                  {anime.external_urls.anilist && (
                    <a
                      href={anime.external_urls.anilist}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center px-3 py-1.5 bg-transparent border border-gray-300 hover:bg-gray-100 text-gray-700 rounded-lg transition-colors text-sm"
                    >
                      AniList
                      <svg
                        className="w-3.5 h-3.5 ml-1.5"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                      >
                        <path
                          strokeLinecap="round"
                          strokeLinejoin="round"
                          strokeWidth={2}
                          d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
                        />
                      </svg>
                    </a>
                  )}
                </div>
              </div>
            </div>
          </div>

          {/* 下半部分: 下载源 */}
          <div className="border-t border-gray-200 p-6 bg-gray-50">
            <h3 className="text-lg font-bold text-gray-900 mb-4">{downloadLabel}</h3>

            {/* Loading Spinner */}
            {loading && <LoadingSpinner />}

            {/* Error Message */}
            {error && <ErrorMessage message={error} />}

            {/* Season Selector */}
            {!loading && !error && seasons.length > 0 && (
              <div className="mb-4">
                <label className="text-sm font-medium text-gray-700">{seasonLabel}</label>
                <select
                  value={selectedSeasonIndex}
                  onChange={handleSeasonChange}
                  className="mt-1 block w-full rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  {seasons.map((season, index) => (
                    <option key={index} value={index}>
                      {season.seasonName} ({season.year})
                    </option>
                  ))}
                </select>
              </div>
            )}

            {/* 筛选下拉栏 */}
            {!loading && !error && filteredItems.length > 0 && (
              <div className="flex gap-3 mb-4 flex-wrap">
                <select
                  value={downloadPreferences.resolution}
                  onChange={(e) => handlePreferenceChange('resolution', e.target.value)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{resolutionLabel}: {allLabel}</option>
                  {availableResolutions.slice(1).map(res => (
                    <option key={res} value={res}>{res}</option>
                  ))}
                </select>

                <select
                  value={downloadPreferences.subgroup}
                  onChange={(e) => handlePreferenceChange('subgroup', e.target.value)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{subgroupLabel}: {allLabel}</option>
                  {availableSubgroups.slice(1).map(sub => (
                    <option key={sub} value={sub}>{sub}</option>
                  ))}
                </select>

                <select
                  value={downloadPreferences.subtitleType}
                  onChange={(e) => handlePreferenceChange('subtitleType', e.target.value)}
                  className="rounded-md border-gray-300 py-2 px-3 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="all">{subtitleLabel}: {allLabel}</option>
                  {availableSubtitleTypes.slice(1).map(type => (
                    <option key={type} value={type}>{type}</option>
                  ))}
                </select>
              </div>
            )}

            {/* 下载列表 */}
            {!loading && !error && filteredItems.length > 0 && (
              <div className="space-y-2 max-h-60 overflow-y-auto">
                {filteredItems.map(item => (
                  <DownloadItem
                    key={item.torrentHash}
                    item={item}
                    isDownloaded={downloadStatus.get(item.torrentHash) || false}
                    onDownload={handleDownload}
                    onCopyMagnet={handleCopyMagnet}
                    isDownloading={downloadingItem === item.torrentHash}
                  />
                ))}
              </div>
            )}

            {/* No items message */}
            {!loading && !error && filteredItems.length === 0 && feedItems.length === 0 && (
              <p className="text-gray-500">{noDownloadLabel}</p>
            )}

            {/* No filter results message */}
            {!loading && !error && filteredItems.length === 0 && feedItems.length > 0 && (
              <p className="text-gray-500">
                {language === 'zh' ? '没有符合条件的下载源' : 'No matching download sources'}
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
