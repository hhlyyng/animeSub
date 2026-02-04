import { useState } from "react";
import type { AnimeInfo } from "./AnimeInfoType";
import { useAppStore } from "../../../stores/useAppStores";

type AnimeCardProps = {
  anime: AnimeInfo;
  onSelect?: () => void;
};

export function AnimeCard({ anime, onSelect }: AnimeCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const language = useAppStore((state) => state.language);

  // Choose title based on language
  const displayTitle = language === 'zh'
    ? (anime.ch_title || anime.en_title)
    : (anime.en_title || anime.ch_title);

  return (
    <div
      className="
                relative cursor-pointer select-none
                transition-transform duration-300
                "
      onClick={() => onSelect?.()}
      onMouseEnter={()=>setIsHovered(true)}
      onMouseLeave={()=>setIsHovered(false)}
    >
      {/* Cover */}
      <div
        className="relative overflow-hidden bg-gray-100 transition-all duration-300 ease-in-out"
        style={{
          height: '300px', // 固定高度
          width: isHovered && anime.images.landscape
            ? '534px'
            : '200px'
        }}
      >
        <img
          src={isHovered && anime.images.landscape
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
