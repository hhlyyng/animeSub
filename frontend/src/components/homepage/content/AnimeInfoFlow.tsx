import { useState, useEffect, useRef } from "react";
import { AnimeCard } from "./AnimeCard";

type AnimeInfo = {
  bangumi_id: string;
  jp_title: string;
  ch_title?: string;
  en_title: string;
  ch_desc?: string;
  en_desc?: string;
  score: string;
  images: {
    portrait: string;
    landscape: string;
  };
  external_urls: {
    bangumi: string;
    tmdb: string;
    anilist: string;
  };
};

type AnimeFlowProps = {
  topic: string;
  items: AnimeInfo[];
};

export function AnimeFlow({ topic, items }: AnimeFlowProps) {
  const [windowSize, setWindowSize] = useState(7); //default window size: 7
  const step = Math.max(1, windowSize - 1);
  const [startIndex, setStartIndex] = useState(0);
  const total = items.length;
  const clampStart = (s: number) => Math.min(Math.max(0, s), Math.max(0, total - windowSize));

  const cardContainerRef = useRef<HTMLDivElement>(null);
  const [titleMargin, setTitleMargin] = useState(0);


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

  useEffect(() => {
    if (cardContainerRef.current) {
      const rect = cardContainerRef.current.getBoundingClientRect();
      const parentRect = cardContainerRef.current.parentElement?.getBoundingClientRect();
      if (parentRect) {
        setTitleMargin(rect.left - parentRect.left);
      }
    }
  }, [canGoLeft, canGoRight, windowSize]);

  const goLeft = () => canGoLeft && setStartIndex(s => clampStart(s - step));
  const goRight = () => canGoRight && setStartIndex(s => clampStart(s + step));

  const visible = items.slice(startIndex, startIndex + windowSize);

  return (
  <div className="w-full min-w-full overflow-hidden block">
    {/* 标题 */}
    <h2 className="text-2xl font-bold text-gray-900 mb-6"
        style={{ marginLeft: `${titleMargin}px` }}>
      {topic}</h2>
    
    {/* 整体容器 - 使用 flex 布局 */}
    <div className="flex w-full items-start gap-4">
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
        // 占位，保持布局对称
      )}
      
      {/* 卡片流容器 */}
      <div className="flex-1 min-w-0 overflow-hidden" ref={cardContainerRef}>
        <div className="flex w-max gap-4 transition-transform duration-500 ease-in-out">
          {visible.map(anime => (
            <div 
              key={anime.bangumi_id} 
              className="flex-shrink-0"
              style={{ minWidth: '200px' }}
            >
              <AnimeCard anime={anime} />
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
                   z-50 "
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
        // 占位，保持布局对称
      )}
    </div>
  </div>
);
}

export default AnimeFlow;