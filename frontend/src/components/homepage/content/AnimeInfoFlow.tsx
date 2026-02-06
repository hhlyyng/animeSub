import { useState, useEffect, useRef } from "react";
import { AnimeCard } from "./AnimeCard";
import { AnimeDetailModal } from "./AnimeDetailModal";
import type { AnimeInfo } from "./AnimeInfoType";
import { useAppStore } from "../../../stores/useAppStores";

type AnimeFlowProps = {
  topic: string;
  items: AnimeInfo[];
};

// 卡片展开时的宽度变化：534px - 200px = 334px
// 父容器居中时的位移补偿：334 / 2 = 167px
const EXPAND_WIDTH_DIFF = 334;
const TITLE_OFFSET_COMPENSATION = EXPAND_WIDTH_DIFF / 2;
// 动画持续时间（与CSS transition-duration匹配）
const ANIMATION_DURATION = 300;

export function AnimeFlow({ topic, items }: AnimeFlowProps) {
  const [windowSize, setWindowSize] = useState(7); //default window size: 7
  const step = Math.max(1, windowSize - 1);
  const [startIndex, setStartIndex] = useState(0);
  const total = items.length;
  const clampStart = (s: number) => Math.min(Math.max(0, s), Math.max(0, total - windowSize));

  // 追踪是否有卡片正在hover并展开
  const [isCardExpanded, setIsCardExpanded] = useState(false);
  // 锁定状态：卡片收缩动画进行中时锁定，阻止其他卡片展开
  const [isHoverLocked, setIsHoverLocked] = useState(false);
  const lockTimeoutRef = useRef<number | null>(null);

  // Modal 状态
  const [selectedAnime, setSelectedAnime] = useState<AnimeInfo | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const setGlobalModalOpen = useAppStore((state) => state.setModalOpen);

  // 处理卡片hover状态变化
  const handleCardHoverChange = (isHovered: boolean, hasLandscape: boolean) => {
    if (isHovered && hasLandscape) {
      // 开始展开
      if (lockTimeoutRef.current) {
        clearTimeout(lockTimeoutRef.current);
        lockTimeoutRef.current = null;
      }
      setIsHoverLocked(false);
      setIsCardExpanded(true);
    } else if (!isHovered && isCardExpanded) {
      // 开始收缩 - 锁定并等待动画完成
      setIsCardExpanded(false);
      setIsHoverLocked(true);
      lockTimeoutRef.current = setTimeout(() => {
        setIsHoverLocked(false);
        lockTimeoutRef.current = null;
      }, ANIMATION_DURATION);
    }
  };

  // 清理timeout
  useEffect(() => {
    return () => {
      if (lockTimeoutRef.current) {
        clearTimeout(lockTimeoutRef.current);
      }
    };
  }, []);

  const handleAnimeSelect = (anime: AnimeInfo) => {
    setSelectedAnime(anime);
    setIsModalOpen(true);
    setGlobalModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedAnime(null);
    setGlobalModalOpen(false);
  };


  useEffect(() => {
    const handleResize = () => {
      if (window.innerWidth < 640) {
        setWindowSize(3); // 手机（<640px）显示 3 个
      } else if (window.innerWidth < 1024) {
        setWindowSize(5); // 平板（640~1024px）显示 4 个
      } else {
        setWindowSize(6); // 桌面 >=1024px 显示 6 个
      }
    };    
    handleResize(); // 初始化
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
    }, []);


  const canGoLeft = startIndex > 0;
  const canGoRight = startIndex + windowSize < total;

  const goLeft = () => canGoLeft && setStartIndex(s => clampStart(s - step));
  const goRight = () => canGoRight && setStartIndex(s => clampStart(s + step));

  const visible = items.slice(startIndex, startIndex + windowSize);

  return (
  <>
    {/* 外层容器 - 控制整个AnimeFlow */}
    <div className="flex flex-col">
      {/* 标题区域 - 左边距与卡片对齐，hover展开时补偿位移 */}
      <div
        className="mb-4 transition-[margin] duration-300 ease-in-out"
        style={{ marginLeft: `${64 + (isCardExpanded ? TITLE_OFFSET_COMPENSATION : 0)}px` }}
      >
        <h2 className="text-2xl font-bold text-gray-900">
          {topic}
        </h2>
      </div>

      {/* 卡片流区域 */}
      <div className="flex items-start gap-4">
        {/* 左按钮 */}
        {canGoLeft ? (
          <button
            onClick={goLeft}
            className="flex-shrink-0 w-12 h-12 flex items-center justify-center
                     bg-white/10 hover:bg-white/90
                     shadow-lg rounded-lg
                     transition-all duration-200
                     backdrop-blur-sm
                     mt-32
                     z-50 relative"
          >
            <svg
              className="w-8 h-8 shrink-0"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2}
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
            </svg>
          </button>
        ) : (
          <div className="w-12 h-12 flex-shrink-0"></div>
        )}

        {/* 卡片流容器 */}
        <div className="flex-1 min-w-0 overflow-visible">
          <div className="flex w-max gap-4 transition-transform duration-500 ease-in-out">
            {visible.map(anime => (
              <div
                key={anime.bangumi_id}
                className="flex-shrink-0"
                style={{ minWidth: '200px' }}
              >
                <AnimeCard
                  anime={anime}
                  onSelect={() => handleAnimeSelect(anime)}
                  onHoverChange={handleCardHoverChange}
                  isHoverLocked={isHoverLocked}
                />
              </div>
            ))}
          </div>
        </div>

        {/* 右按钮 */}
        {canGoRight ? (
          <button
            onClick={goRight}
            className="flex-shrink-0 w-12 h-12 flex items-center justify-center
                     bg-white/10 hover:bg-white/90
                     shadow-lg rounded-lg
                     transition-all duration-200
                     backdrop-blur-sm
                     mt-32
                     z-50"
          >
            <svg
              className="w-8 h-8 shrink-0"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2}
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
            </svg>
          </button>
        ) : (
          <div className="w-12 h-12 flex-shrink-0"></div>
        )}
      </div>
    </div>

    {/* Modal */}
    {selectedAnime && (
      <AnimeDetailModal
        anime={selectedAnime}
        open={isModalOpen}
        onClose={handleCloseModal}
      />
    )}
  </>
);
}

export default AnimeFlow;