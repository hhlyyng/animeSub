import { useState, useEffect } from "react";
import AnimeFlow from "./AnimeInfoFlow";
import { useAppStore } from "../../../stores/useAppStores";

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

const CACHE_ANIMELIST= 'todayAnimes';

const HomeContent = () => {
    const [animes, setAnimes] = useState<AnimeInfo[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const language = useAppStore((state) => state.language);

    // 根据语言设置文本
    const texts = {
        loading: language === 'zh' ? '正在加载今日动漫...' : "Loading today's anime...",
        error: language === 'zh' ? '错误' : 'Error',
        noAnime: language === 'zh' ? '今日暂无动漫' : 'No anime found for today',
        topic: language === 'zh' ? '今日放送' : "Today's Anime"
    };

    useEffect(() => {
        // 尝试从 sessionStorage 读取缓存
        const cached = sessionStorage.getItem(CACHE_ANIMELIST);
        if (cached) {
            try {
                const parsedData = JSON.parse(cached);
                setAnimes(parsedData);
                setLoading(false);
                return;
            } catch (e) {
                console.error('Failed to parse cached data:', e);
                sessionStorage.removeItem(CACHE_ANIMELIST);
            }
        }
        
        const fetchTodayAnime = async () => {
            try {
                setLoading(true);
                setError(null);
                const response = await fetch('http://localhost:5072/api/anime/today', {
                    method: 'GET',
                    headers: {
                        'X-Bangumi-Token': 'KCph7wQKkv5DAtOeznpzFCDo3eaNkQrMqotnvyEX',
                        'X-TMDB-Token': 'REMOVED',
                        'Content-Type': 'application/json'
                    }
                });
                
                if (!response.ok) {
                    throw new Error(`HTTP error! status: ${response.status}`);
                }
                
                const result = await response.json();
                
                if (!result.success) {
                    throw new Error(result.message);
                }
                
                // 保存到 sessionStorage
                sessionStorage.setItem(CACHE_ANIMELIST, JSON.stringify(result.data.animes));
                setAnimes(result.data.animes);
            } catch (err) {
                setError(err instanceof Error ? err.message : 'Unknown error');
                console.error('API Error:', err);
            } finally {
                setLoading(false);
            }
        };
        
        fetchTodayAnime();
    }, []);

    if (loading) {
        return (
            <div className="w-full px-12 flex justify-center items-center h-64">
                <div className="text-lg">{texts.loading}</div>
            </div>
        );
    }

    if (error) {
        return (
            <div className="w-full px-12 flex justify-center items-center h-64">
                <div className="text-red-500">{texts.error}: {error}</div>
            </div>
        );
    }

    if (animes.length === 0) {
        return (
            <div className="w-full flex justify-center items-center h-64">
                <div className="text-gray-500">{texts.noAnime}</div>
            </div>
        );
    }

    return (
        <div className="w-fit overflow-hidden content-header">
            <AnimeFlow topic={texts.topic} items={animes} />
        </div>
    );
};

export default HomeContent;