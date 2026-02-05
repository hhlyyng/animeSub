import { useState, useEffect } from "react";
import type { AnimeInfo } from "./AnimeInfoType";
import { useAppStore } from "../../../stores/useAppStores";

type AnimeCardProps = {
  anime: AnimeInfo;
  onSelect?: () => void;
  onHoverChange?: (isHovered: boolean, hasLandscape: boolean) => void;
  isHoverLocked?: boolean;
};

export function AnimeCard({ anime, onSelect, onHoverChange, isHoverLocked }: AnimeCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const language = useAppStore((state) => state.language);

  // Choose title based on language, fallback to jp_title if both ch/en are empty
  const displayTitle = language === 'zh'
    ? (anime.ch_title || anime.en_title || anime.jp_title)
    : (anime.en_title || anime.ch_title || anime.jp_title);

  const hasLandscape = !!anime.images.landscape;

  // 实际是否展开：需要hover且未被锁定（或者是自己触发的锁定）
  const shouldExpand = isHovered && !isHoverLocked;

  // 当锁定解除且鼠标正在hover时，通知父组件展开
  useEffect(() => {
    if (!isHoverLocked && isHovered && hasLandscape) {
      onHoverChange?.(true, hasLandscape);
    }
  }, [isHoverLocked, isHovered, hasLandscape, onHoverChange]);

  const handleMouseEnter = () => {
    setIsHovered(true);
    // 只有不锁定时才通知父组件展开
    if (!isHoverLocked) {
      onHoverChange?.(true, hasLandscape);
    }
  };

  const handleMouseLeave = () => {
    setIsHovered(false);
    onHoverChange?.(false, hasLandscape);
  };

  return (
    <div
      className="
                relative cursor-pointer select-none
                transition-transform duration-300
                "
      onClick={() => onSelect?.()}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
    >
      {/* Cover */}
      <div
        className="relative overflow-hidden bg-gray-100 transition-all duration-300 ease-in-out"
        style={{
          height: '300px', // 固定高度
          width: shouldExpand && anime.images.landscape
            ? '534px'
            : '200px'
        }}
      >
        <img
          src={shouldExpand && anime.images.landscape
               ? anime.images.landscape
               : anime.images.portrait}
          alt={displayTitle}
          className="absolute inset-0 h-full w-full object-cover transition-opacity duration-300"
        />
      </div>

      {/* title + score*/}
      <div className="mt-2 text-sm font-semibold text-gray-900 break-all"
            style={{ width: '200px' }}>
        {displayTitle}
      </div>
    </div>
  );
}

export default AnimeCard;
