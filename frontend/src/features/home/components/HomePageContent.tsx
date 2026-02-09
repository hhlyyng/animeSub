import { useState, useEffect } from "react";
import AnimeFlow from "./AnimeInfoFlow";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";
import { API_BASE_URL } from "../../../config/env";

const CACHE_KEYS = {
    todayAnimes: 'v2:todayAnimes',
    bangumiTop10: 'v2:bangumiTop10',
    anilistTop10: 'v2:anilistTop10',
    malTop10: 'v2:malTop10'
};

const API_BASE = `${API_BASE_URL}/anime`;

// Preload images in background for smoother UX
const preloadImages = (animes: AnimeInfo[]) => {
    const imageUrls = animes
        .map(anime => anime.images.landscape)
        .filter(url => url && url.length > 0);

    // Use requestIdleCallback for non-blocking preload, fallback to setTimeout
    const schedulePreload = window.requestIdleCallback || ((cb) => setTimeout(cb, 1));

    schedulePreload(() => {
        imageUrls.forEach(url => {
            const img = new Image();
            img.src = url;
        });
    });
};

const HomeContent = () => {
    const [todayAnimes, setTodayAnimes] = useState<AnimeInfo[]>([]);
    const [bangumiTop10, setBangumiTop10] = useState<AnimeInfo[]>([]);
    const [anilistTop10, setAnilistTop10] = useState<AnimeInfo[]>([]);
    const [malTop10, setMalTop10] = useState<AnimeInfo[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const language = useAppStore((state) => state.language);

    const texts = {
        loading: language === 'zh' ? '正在加载动漫数据...' : "Loading anime data...",
        error: language === 'zh' ? '错误' : 'Error',
        noAnime: language === 'zh' ? '今日暂无动漫' : 'No anime found for today',
        todayTopic: language === 'zh' ? '今日放送' : "Today's Anime",
        bangumiTopic: 'Bangumi Top 10',
        anilistTopic: 'AniList Top 10',
        malTopic: 'MAL Top 10'
    };

    // 通用的获取数据函数
    const fetchAnimeData = async (
        endpoint: string,
        cacheKey: string
    ): Promise<AnimeInfo[]> => {
        // 尝试从缓存读取
        const cached = sessionStorage.getItem(cacheKey);
        if (cached) {
            try {
                return JSON.parse(cached);
            } catch (e) {
                console.error(`Failed to parse cached data for ${cacheKey}:`, e);
                sessionStorage.removeItem(cacheKey);
            }
        }

        // 从 API 获取
        const response = await fetch(`${API_BASE}${endpoint}`, {
            method: 'GET'
        });

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const result = await response.json();

        if (!result.success) {
            throw new Error(result.message);
        }

        // 保存到缓存
        sessionStorage.setItem(cacheKey, JSON.stringify(result.data.animes));
        return result.data.animes;
    };

    useEffect(() => {
        const fetchAllData = async () => {
            try {
                setLoading(true);
                setError(null);

                // 并行获取所有数据 (all endpoints need TMDB token for backdrop URLs)
                const [today, bangumi, anilist, mal] = await Promise.all([
                    fetchAnimeData('/today', CACHE_KEYS.todayAnimes),
                    fetchAnimeData('/top/bangumi', CACHE_KEYS.bangumiTop10),
                    fetchAnimeData('/top/anilist', CACHE_KEYS.anilistTop10),
                    fetchAnimeData('/top/mal', CACHE_KEYS.malTop10)
                ]);

                setTodayAnimes(today);
                setBangumiTop10(bangumi);
                setAnilistTop10(anilist);
                setMalTop10(mal);

                // Preload landscape images for smoother hover experience
                const allAnimes = [...today, ...bangumi, ...anilist, ...mal];
                preloadImages(allAnimes);
            } catch (err) {
                setError(err instanceof Error ? err.message : 'Unknown error');
                console.error('API Error:', err);
            } finally {
                setLoading(false);
            }
        };

        fetchAllData();
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

    if (todayAnimes.length === 0 && bangumiTop10.length === 0 && anilistTop10.length === 0 && malTop10.length === 0) {
        return (
            <div className="w-full flex justify-center items-center h-64">
                <div className="text-gray-500">{texts.noAnime}</div>
            </div>
        );
    }

    return (
        <div className="content-header w-full">
            {todayAnimes.length > 0 && (
                <div className="anime-flow-container">
                    <AnimeFlow topic={texts.todayTopic} items={todayAnimes} />
                </div>
            )}
            {bangumiTop10.length > 0 && (
                <div className="anime-flow-container">
                    <AnimeFlow topic={texts.bangumiTopic} items={bangumiTop10} />
                </div>
            )}
            {anilistTop10.length > 0 && (
                <div className="anime-flow-container">
                    <AnimeFlow topic={texts.anilistTopic} items={anilistTop10} />
                </div>
            )}
            {malTop10.length > 0 && (
                <div className="anime-flow-container">
                    <AnimeFlow topic={texts.malTopic} items={malTop10} />
                </div>
            )}
        </div>
    );
};

export default HomeContent;
