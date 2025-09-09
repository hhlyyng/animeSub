import React, { useState, useEffect } from "react";
import { AnimeCard } from "./AnimeCard";

type AnimeInfo = {
  id: string;
  ch_title: string;
  en_title: string;
  ch_desc?: string;
  en_desc?: string;
  score: string;
  images: {
    large: string;
    medium: string;
    small: string;
    grid: string;
    common: string;
  };
};

type AnimeFlowProps = {
  topic: string;
  items: AnimeInfo[];
};

export function AnimeFlow({ topic, items }: AnimeFlowProps) {
  const [windowSize, setWindowSize] = useState(7); // 默认 6
  const step = Math.max(1, windowSize - 1);
  const [startIndex, setStartIndex] = useState(0);
  const total = items.length;
  const clampStart = (s: number) => Math.min(Math.max(0, s), Math.max(0, total - windowSize));
    // 固定显示6张卡片

  useEffect(() => {
    const handleResize = () => {
      if (window.innerWidth < 640) {
        setWindowSize(3); // 手机（<640px）显示 3 个
      } else if (window.innerWidth < 1024) {
        setWindowSize(5); // 平板（640~1024px）显示 4 个
      } else {
        setWindowSize(7); // 桌面 >=1024px 显示 6 个
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
    <div className="w-full">
      {/* 标题 */}
      <h2 className="text-2xl font-bold text-gray-900 mb-6">{topic}</h2>
      
      {/* 卡片容器 */}
      <div className="relative">
        {/* 左按钮 */}
        {canGoLeft && (
          <button
            onClick={goLeft}
            className="absolute left-0 top-1/2 -translate-y-1/2 z-20 
                     bg-white/90 hover:bg-white shadow-lg rounded-full p-2
                     transition-all duration-200"
          >
            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
            </svg>
          </button>
        )}
        
        {/* 右按钮 */}
        {canGoRight && (
          <button
            onClick={goRight}
            className="absolute right-0 top-1/2 -translate-y-1/2 z-20
                     bg-white/90 hover:bg-white shadow-lg rounded-full p-2
                     transition-all duration-200"
          >
            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
            </svg>
          </button>
        )}
        
        {/* 卡片流容器 */}
        <div className="overflow-hidden mx-8">
          <div
            className="flex gap-4 transition-transform duration-500 ease-in-out"
          >
            {visible.map(anime => (
              <div key={anime.id} className="flex-shrink-0 w-40 sm:w-44 md:w-48">
                <AnimeCard anime={anime} />
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

export default AnimeFlow;