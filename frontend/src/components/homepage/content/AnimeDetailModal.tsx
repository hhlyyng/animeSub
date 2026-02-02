import { useEffect } from "react";
import type { AnimeInfo } from "./AnimeInfoType";

type AnimeDetailModalProps = {
  anime: AnimeInfo;
  open: boolean;
  onClose: () => void;
};

export function AnimeDetailModal({ anime, open, onClose }: AnimeDetailModalProps) {
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

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      onClick={onClose}
    >
      {/* 背景遮罩 */}
      <div className="absolute inset-0 bg-black/50 backdrop-blur-md" />
      
      {/* Modal 内容 */}
      <div
        className="relative z-10 w-full max-w-4xl bg-white rounded-lg shadow-2xl overflow-hidden animate-fadeIn"
        onClick={(e) => e.stopPropagation()}
      >
        {/* 关闭按钮 */}
        <button
          onClick={onClose}
          className="absolute top-4 right-4 z-20 w-8 h-8 flex items-center justify-center rounded-full bg-black/20 hover:bg-black/40 text-white transition-colors"
          aria-label="Close"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            className="h-5 w-5"
            viewBox="0 0 20 20"
            fill="currentColor"
          >
            <path
              fillRule="evenodd"
              d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
              clipRule="evenodd"
            />
          </svg>
        </button>

        <div className="flex flex-col md:flex-row max-h-[90vh] overflow-hidden">
          {/* 左侧：图片 */}
          <div className="md:w-2/5 bg-gray-100 flex-shrink-0">
            <img
              src={anime.images.portrait}
              alt={anime.ch_title || anime.en_title}
              className="w-full h-full object-cover"
            />
          </div>

          {/* 右侧：详情 */}
          <div className="md:w-3/5 p-6 md:p-8 overflow-y-auto">
            {/* 标题区域 */}
            <div className="mb-6">
              <h2 className="text-2xl md:text-3xl font-bold text-gray-900 mb-2">
                {anime.ch_title || anime.en_title}
              </h2>
              {anime.ch_title && anime.en_title && (
                <p className="text-lg text-gray-600">{anime.en_title}</p>
              )}
              {anime.jp_title && (
                <p className="text-sm text-gray-500 mt-1">{anime.jp_title}</p>
              )}
            </div>

            {/* 评分 */}
            <div className="mb-6">
              <div className="inline-flex items-center px-3 py-1 bg-yellow-100 rounded-full">
                <svg
                  className="w-5 h-5 text-yellow-500 mr-1"
                  fill="currentColor"
                  viewBox="0 0 20 20"
                >
                  <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
                </svg>
                <span className="text-lg font-semibold text-gray-900">
                  {anime.score}
                </span>
              </div>
            </div>

            {/* 描述 */}
            <div className="mb-6">
              <h3 className="text-lg font-semibold text-gray-900 mb-3">简介</h3>
              <p className="text-gray-700 leading-relaxed whitespace-pre-wrap">
                {anime.ch_desc || anime.en_desc || "暂无描述"}
              </p>
            </div>

            {/* 外部链接 */}
            <div>
              <h3 className="text-lg font-semibold text-gray-900 mb-3">相关链接</h3>
              <div className="flex flex-wrap gap-3">
                {anime.external_urls.bangumi && (
                  
                    href={anime.external_urls.bangumi}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center px-4 py-2 bg-pink-500 hover:bg-pink-600 text-white rounded-lg transition-colors"
                  >
                    Bangumi
                    <svg
                      className="w-4 h-4 ml-2"
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
                  
                    href={anime.external_urls.tmdb}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center px-4 py-2 bg-blue-500 hover:bg-blue-600 text-white rounded-lg transition-colors"
                  >
                    TMDB
                    <svg
                      className="w-4 h-4 ml-2"
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
                  
                    href={anime.external_urls.anilist}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center px-4 py-2 bg-indigo-500 hover:bg-indigo-600 text-white rounded-lg transition-colors"
                  >
                    AniList
                    <svg
                      className="w-4 h-4 ml-2"
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
      </div>
    </div>
  );
}

export default AnimeDetailModal;