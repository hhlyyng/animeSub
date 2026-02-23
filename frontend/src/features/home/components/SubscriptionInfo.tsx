import { useEffect, useMemo, useState } from "react";
import type { AnimeInfo } from "../../../types/anime";

const CARD_BASE_WIDTH = 200;
const FLOW_GAP = 16;
const NAV_BUTTON_SIZE = 48;
const TITLE_BASE_OFFSET = NAV_BUTTON_SIZE + FLOW_GAP; // 64px, keep aligned with AnimeInfoFlow title anchor

type SubscriptionInfoProps = {
  topic: string;
  items: AnimeInfo[];
  language: "zh" | "en";
  onSelect: (anime: AnimeInfo) => void;
  emptyText: string;
};

function resolveCardKey(anime: AnimeInfo, index: number): string {
  return (
    anime.external_urls?.mal?.trim() ||
    anime.external_urls?.anilist?.trim() ||
    anime.external_urls?.bangumi?.trim() ||
    `${anime.bangumi_id || "unknown"}|${anime.jp_title || ""}|${anime.en_title || ""}|${anime.ch_title || ""}|${index}`
  );
}

function resolveDisplayTitle(anime: AnimeInfo, language: "zh" | "en"): string {
  const zhTitle = anime.ch_title?.trim() || anime.en_title?.trim() || anime.jp_title?.trim();
  const enTitle = anime.en_title?.trim() || anime.ch_title?.trim() || anime.jp_title?.trim();
  return language === "zh" ? zhTitle || anime.bangumi_id : enTitle || anime.bangumi_id;
}

export function SubscriptionInfo({ topic, items, language, onSelect, emptyText }: SubscriptionInfoProps) {
  const [windowSize, setWindowSize] = useState(6);

  useEffect(() => {
    const handleResize = () => {
      if (window.innerWidth < 640) {
        setWindowSize(3);
      } else if (window.innerWidth < 1024) {
        setWindowSize(5);
      } else {
        setWindowSize(6);
      }
    };

    handleResize();
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, []);

  const gridStyle = useMemo(
    () => ({
      gridTemplateColumns: `repeat(${windowSize}, ${CARD_BASE_WIDTH}px)`,
    }),
    [windowSize]
  );
  const trackBaseWidth = useMemo(
    () => windowSize * CARD_BASE_WIDTH + Math.max(0, windowSize - 1) * FLOW_GAP,
    [windowSize]
  );
  const flowBaseWidth = useMemo(
    () => trackBaseWidth + NAV_BUTTON_SIZE * 2 + FLOW_GAP * 2,
    [trackBaseWidth]
  );

  return (
    <div className="w-full">
      <div className="w-fit flex flex-col gap-4" style={{ marginLeft: `calc(50% - ${flowBaseWidth / 2}px)` }}>
        <h2 className="text-2xl font-bold text-gray-900" style={{ marginLeft: `${TITLE_BASE_OFFSET}px` }}>
          {topic}
        </h2>

        {items.length === 0 ? (
          <p
            className="rounded-lg border border-gray-200 bg-gray-50 p-4 text-gray-600"
            style={{ marginLeft: `${TITLE_BASE_OFFSET}px`, width: `${trackBaseWidth}px` }}
          >
            {emptyText}
          </p>
        ) : (
          <div className="overflow-visible" style={{ marginLeft: `${TITLE_BASE_OFFSET}px`, width: `${trackBaseWidth}px` }}>
            <div className="grid gap-4" style={gridStyle}>
              {items.map((anime, index) => {
                const title = resolveDisplayTitle(anime, language);
                const cover = anime.images?.portrait?.trim() || anime.images?.landscape?.trim() || "";

                return (
                  <article
                    key={resolveCardKey(anime, index)}
                    className="min-w-0 cursor-pointer select-none outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
                    role="button"
                    tabIndex={0}
                    onClick={() => onSelect(anime)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        onSelect(anime);
                      }
                    }}
                  >
                    <div className="overflow-hidden bg-gray-100">
                      {cover ? (
                        <img
                          src={cover}
                          alt={title}
                          className="aspect-[2/3] w-full object-cover"
                          loading="lazy"
                        />
                      ) : (
                        <div className="aspect-[2/3] w-full bg-gray-200" />
                      )}
                    </div>
                    <div className="mt-2 min-h-10 break-words text-sm font-semibold text-gray-900">
                      {title}
                    </div>
                  </article>
                );
              })}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default SubscriptionInfo;
