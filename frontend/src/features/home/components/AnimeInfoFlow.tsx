import { useState, useEffect, useRef } from "react";
import { AnimeCard } from "./AnimeCard";
import { AnimeDetailModal } from "./AnimeDetailModal";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";
import ShuffleIcon from "../../../components/icons/ShuffleIcon";

type AnimeFlowProps = {
  topic: string;
  items: AnimeInfo[];
  onRefresh?: () => void;
};

// 卡片展开时的宽度变化：534px - 200px = 334px
// 父容器居中时的位移补偿：334 / 2 = 167px
// 动画持续时间（与CSS transition-duration匹配）
const CARD_BASE_WIDTH = 200;
const CARD_EXPAND_DIFF = 334;
const CARD_HALF_EXPAND_DIFF = CARD_EXPAND_DIFF / 2;
const FLOW_GAP = 16;
const NAV_BUTTON_SIZE = 48;
const TITLE_BASE_OFFSET = NAV_BUTTON_SIZE + FLOW_GAP;
const ANIMATION_DURATION = 300;

export function AnimeFlow({ topic, items, onRefresh }: AnimeFlowProps) {
  const [windowSize, setWindowSize] = useState(7); //default window size: 7
  const step = Math.max(1, windowSize - 1);
  const [startIndex, setStartIndex] = useState(0);
  const total = items.length;
  const clampStart = (s: number) => Math.min(Math.max(0, s), Math.max(0, total - windowSize));

  // 追踪是否有卡片正在hover并展开
  const [isCardExpanded, setIsCardExpanded] = useState(false);
  const [expandedCardIndex, setExpandedCardIndex] = useState<number | null>(null);
  // 锁定状态：卡片收缩动画进行中时锁定，阻止其他卡片展开
  const [isHoverLocked, setIsHoverLocked] = useState(false);
  const lockTimeoutRef = useRef<number | null>(null);

  // Modal 状态
  const [selectedAnime, setSelectedAnime] = useState<AnimeInfo | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const setGlobalModalOpen = useAppStore((state) => state.setModalOpen);
  const language = useAppStore((state) => state.language);
  const [isRefreshHovered, setIsRefreshHovered] = useState(false);

  // 处理卡片hover状态变化
  const handleCardHoverChange = (isHovered: boolean, hasLandscape: boolean, cardIndex: number) => {
    if (isHovered && hasLandscape) {
      // 开始展开
      if (lockTimeoutRef.current) {
        clearTimeout(lockTimeoutRef.current);
        lockTimeoutRef.current = null;
      }
      setIsHoverLocked(false);
      setIsCardExpanded(true);
      setExpandedCardIndex(cardIndex);
    } else if (!isHovered && isCardExpanded) {
      // 开始收缩 - 锁定并等待动画完成
      setIsCardExpanded(false);
      setExpandedCardIndex(null);
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
  const visibleCount = Math.min(windowSize, total);
  const trackBaseWidth = visibleCount * CARD_BASE_WIDTH + Math.max(0, visibleCount - 1) * FLOW_GAP;
  const flowBaseWidth = trackBaseWidth + NAV_BUTTON_SIZE * 2 + FLOW_GAP * 2;
  const navSlotClassName = "mt-32 flex h-12 w-12 flex-shrink-0 items-center justify-center";
  const navButtonClassName = `inline-flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-lg bg-white/10 p-0
    transition-all duration-200 backdrop-blur-sm
    hover:bg-white/90 border-0 shadow-lg`;
  const resolveExpandDirection = (index: number): "right" | "center" | "left" => {
    if (index <= 0) {
      return "right";
    }
    if (index >= visible.length - 1) {
      return "left";
    }
    return "center";
  };
  const expandedDirection =
    expandedCardIndex !== null ? resolveExpandDirection(expandedCardIndex) : null;
  const shouldHideLeftNav =
    isCardExpanded && (expandedDirection === "center" || expandedDirection === "left");
  const leftNavTranslateX =
    expandedDirection === "left"
      ? -CARD_EXPAND_DIFF
      : expandedDirection === "center"
        ? -CARD_HALF_EXPAND_DIFF
        : 0;
  const resolveCardWrapperShift = (index: number): number => {
    if (!isCardExpanded || expandedCardIndex === null || index === expandedCardIndex || !expandedDirection) {
      return 0;
    }
    if (expandedDirection === "center") {
      return -CARD_HALF_EXPAND_DIFF;
    }
    if (expandedDirection === "left" && index < expandedCardIndex) {
      return -CARD_EXPAND_DIFF;
    }
    return 0;
  };
  useEffect(() => {
    if (expandedCardIndex !== null && expandedCardIndex >= visible.length) {
      setExpandedCardIndex(null);
      setIsCardExpanded(false);
    }
  }, [expandedCardIndex, visible.length]);

  const resolveCardKey = (anime: AnimeInfo, index: number): string => {
    return (
      anime.external_urls?.mal?.trim() ||
      anime.external_urls?.anilist?.trim() ||
      anime.external_urls?.bangumi?.trim() ||
      `${anime.bangumi_id || "unknown"}|${anime.jp_title || ""}|${anime.en_title || ""}|${anime.ch_title || ""}|${startIndex + index}`
    );
  };

  return (
  <>
    {/* 外层容器 - 控制整个AnimeFlow */}
    <div className="w-full">
      <div
        className="w-fit flex flex-col"
        style={{ marginLeft: `calc(50% - ${flowBaseWidth / 2}px)` }}
      >
      {/* 标题区域 - 左边距与卡片对齐，hover展开时补偿位移 */}
      <div className="mb-4 flex items-center gap-2" style={{ marginLeft: `${TITLE_BASE_OFFSET}px` }}>
        <h2 className="text-2xl font-bold text-gray-900">
          {topic}
        </h2>
        {onRefresh && (
          <button
            type="button"
            onClick={onRefresh}
            title={language === "zh" ? "换一批" : "Shuffle"}
            onMouseEnter={() => setIsRefreshHovered(true)}
            onMouseLeave={() => setIsRefreshHovered(false)}
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              width: "28px",
              height: "28px",
              backgroundColor: "white",
              border: "none",
              borderRadius: "6px",
              cursor: "pointer",
              color: "black",
              padding: "6px",
              flexShrink: 0,
              transform: isRefreshHovered ? "scale(1.3)" : "scale(1)",
              transition: "transform 0.15s ease",
            }}
          >
            <ShuffleIcon />
          </button>
        )}
      </div>

      {/* 卡片流区域 */}
      <div className="flex w-fit items-start gap-4">
        {/* 左按钮 */}
        <div
          className={`${navSlotClassName} transition-[transform,opacity] duration-300 ease-in-out`}
          style={{
            transform: `translateX(${leftNavTranslateX}px)`,
            opacity: shouldHideLeftNav ? 0 : 1,
            pointerEvents: shouldHideLeftNav ? "none" : "auto",
          }}
        >
          {canGoLeft ? (
            <button
              onClick={goLeft}
              className={`${navButtonClassName} z-50`}
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
          ) : null}
        </div>

        {/* 卡片流容器 */}
        <div className="min-w-0 w-max overflow-visible">
          <div className="flex w-max gap-4 transition-transform duration-500 ease-in-out">
            {visible.map((anime, index) => (
              <div
                key={resolveCardKey(anime, index)}
                className="flex-shrink-0 transition-transform duration-300 ease-in-out"
                style={{
                  minWidth: `${CARD_BASE_WIDTH}px`,
                  transform: `translateX(${resolveCardWrapperShift(index)}px)`,
                }}
              >
                {/** Expand direction: first -> right, middle -> both sides, last -> left */}
                <AnimeCard
                  anime={anime}
                  onSelect={() => handleAnimeSelect(anime)}
                  onHoverChange={(isHovered, hasLandscape) =>
                    handleCardHoverChange(isHovered, hasLandscape, index)
                  }
                  isHoverLocked={isHoverLocked}
                  expandDirection={resolveExpandDirection(index)}
                />
              </div>
            ))}
          </div>
        </div>

        {/* 右按钮 */}
        <div className={navSlotClassName}>
          {canGoRight ? (
            <button
              onClick={goRight}
              className={`${navButtonClassName} z-50`}
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
          ) : null}
        </div>
      </div>
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
