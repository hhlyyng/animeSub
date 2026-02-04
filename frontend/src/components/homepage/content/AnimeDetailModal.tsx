import { useEffect } from "react";
import { createPortal } from "react-dom";
import type { AnimeInfo } from "./AnimeInfoType";
import { useAppStore } from "../../../stores/useAppStores";

type AnimeDetailModalProps = {
  anime: AnimeInfo;
  open: boolean;
  onClose: () => void;
};

export function AnimeDetailModal({ anime, open, onClose }: AnimeDetailModalProps) {
  const language = useAppStore((state) => state.language);

  // 根据语言选择标题和描述
  const primaryTitle = language === 'zh'
    ? (anime.ch_title || anime.en_title)
    : (anime.en_title || anime.ch_title);

  const secondaryTitle = anime.jp_title;

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
        {/* 关闭按钮 */}
        <button
          onClick={onClose}
          className="group"
          style={{
            all: 'unset',
            position: 'absolute',
            top: '1rem',
            right: '1rem',
            zIndex: 20,
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
              {/* 标题和分数 */}
              <div className="mb-4 pr-12">
                <div className="flex items-center gap-3 mb-1">
                  <h2 className="text-2xl font-bold text-gray-900">
                    {primaryTitle}
                  </h2>
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
                </div>
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
            <h3 className="text-lg font-bold text-gray-900 mb-2">{downloadLabel}</h3>
            <p className="text-gray-500">{noDownloadLabel}</p>
          </div>
        </div>
      </div>
    </div>
  );

  return createPortal(modalContent, document.body);
}

export default AnimeDetailModal;
