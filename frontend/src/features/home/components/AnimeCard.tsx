import { useState, useEffect } from "react";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";

type AnimeCardProps = {
  anime: AnimeInfo;
  onSelect?: () => void;
  onHoverChange?: (isHovered: boolean, hasLandscape: boolean) => void;
  isHoverLocked?: boolean;
  expandDirection?: "right" | "center" | "left";
  disableExpand?: boolean;
};

const BASE_COVER_WIDTH = 200;
const EXPANDED_COVER_WIDTH = 534;
const EXPAND_WIDTH_DIFF = EXPANDED_COVER_WIDTH - BASE_COVER_WIDTH;
const TITLE_LINE_HEIGHT_PX = 20;
const TITLE_MAX_LINES = 2;
const TITLE_BLOCK_HEIGHT_PX = TITLE_LINE_HEIGHT_PX * TITLE_MAX_LINES;

function normalizeTitleForDisplay(title: string): string {
  return title.replace(/\u301C/g, "~").trim();
}

export function AnimeCard({
  anime,
  onSelect,
  onHoverChange,
  isHoverLocked,
  expandDirection = "right",
  disableExpand = false,
}: AnimeCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const language = useAppStore((state) => state.language);

  // Choose title based on language, fallback to jp_title if both ch/en are empty
  const rawDisplayTitle = language === "zh"
    ? (anime.ch_title || anime.en_title || anime.jp_title)
    : (anime.en_title || anime.ch_title || anime.jp_title);
  const displayTitle = normalizeTitleForDisplay(rawDisplayTitle);

  // effectiveLandscape = false when disableExpand, so ALL expand-related logic
  // (width, translateX, onHoverChange callbacks) treats this card as having no
  // landscape image — preventing both self-expansion and sibling wrapper shifts.
  const effectiveLandscape = !disableExpand && !!anime.images?.landscape;

  const shouldExpand = isHovered && !isHoverLocked && effectiveLandscape;
  const coverTranslateX =
    shouldExpand
      ? expandDirection === "left"
        ? -EXPAND_WIDTH_DIFF
        : expandDirection === "center"
          ? -EXPAND_WIDTH_DIFF / 2
          : 0
      : 0;

  useEffect(() => {
    if (!isHoverLocked && isHovered && effectiveLandscape) {
      onHoverChange?.(true, effectiveLandscape);
    }
  }, [isHoverLocked, isHovered, effectiveLandscape, onHoverChange]);

  const handleMouseEnter = () => {
    setIsHovered(true);
    if (!isHoverLocked) {
      onHoverChange?.(true, effectiveLandscape);
    }
  };

  const handleMouseLeave = () => {
    setIsHovered(false);
    onHoverChange?.(false, effectiveLandscape);
  };

  return (
    <div
      className="relative cursor-pointer select-none transition-transform duration-300"
      style={{ zIndex: shouldExpand ? 10 : 0 }}
      onClick={() => onSelect?.()}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
    >
      {/* Cover */}
      <div
        className="relative overflow-hidden bg-gray-100 transition-[width,transform] duration-300 ease-in-out"
        style={{
          height: "300px",
          width: shouldExpand
            ? `${EXPANDED_COVER_WIDTH}px`
            : `${BASE_COVER_WIDTH}px`,
          transform: `translateX(${coverTranslateX}px)`,
        }}
      >
        <img
          src={shouldExpand
            ? anime.images.landscape
            : anime.images?.portrait}
          alt={displayTitle}
          className="absolute inset-0 h-full w-full object-cover transition-opacity duration-300"
        />
      </div>

      {/* Title */}
      <div
        className="mt-2 break-all text-sm font-semibold text-gray-900 transition-[width,transform] duration-300 ease-in-out"
        style={{
          width: shouldExpand
            ? `${EXPANDED_COVER_WIDTH}px`
            : `${BASE_COVER_WIDTH}px`,
          transform: `translateX(${coverTranslateX}px)`,
          lineHeight: `${TITLE_LINE_HEIGHT_PX}px`,
          minHeight: `${TITLE_BLOCK_HEIGHT_PX}px`,
          maxHeight: `${TITLE_BLOCK_HEIGHT_PX}px`,
          overflow: "hidden",
          display: "-webkit-box",
          WebkitLineClamp: TITLE_MAX_LINES,
          WebkitBoxOrient: "vertical",
        }}
      >
        {displayTitle}
      </div>
    </div>
  );
}

export default AnimeCard;
