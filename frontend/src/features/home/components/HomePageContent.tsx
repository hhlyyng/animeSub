import { useState, useEffect } from "react";
import AnimeFlow from "./AnimeInfoFlow";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";
import { API_BASE_URL } from "../../../config/env";
import { authFetch } from "../../../services/apiClient";

const CACHE_KEYS = {
    todayAnimes: 'v3:todayAnimes',
    bangumiTop10: 'v3:bangumiTop10',
    anilistTop10: 'v3:anilistTop10',
    malTop10: 'v3:malTop10'
};

const API_BASE = `${API_BASE_URL}/anime`;

const resolveAnimeIdentity = (anime: AnimeInfo): string => {
    return (
        anime.external_urls?.mal?.trim() ||
        anime.external_urls?.anilist?.trim() ||
        anime.external_urls?.bangumi?.trim() ||
        `${anime.bangumi_id?.trim() ?? ''}|${anime.jp_title?.trim() ?? ''}|${anime.en_title?.trim() ?? ''}|${anime.ch_title?.trim() ?? ''}`
    );
};

const dedupeAnimeList = (animes: AnimeInfo[]): AnimeInfo[] => {
    const seen = new Set<string>();
    return animes.filter((anime) => {
        const identity = resolveAnimeIdentity(anime);
        if (!identity) {
            return true;
        }

        if (seen.has(identity)) {
            return false;
        }

        seen.add(identity);
        return true;
    });
};

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
        loading: language === "zh" ? "\u6b63\u5728\u52a0\u8f7d\u52a8\u6f2b\u6570\u636e..." : "Loading anime data...",
        error: language === "zh" ? "\u9519\u8bef" : "Error",
        noAnime: language === "zh" ? "\u4eca\u65e5\u6682\u65e0\u52a8\u6f2b" : "No anime found for today",
        todayTopic: language === "zh" ? "\u4eca\u65e5\u653e\u9001" : "Today's Anime",
        bangumiTopic: "Bangumi Top 10",
        anilistTopic: "AniList Top 10",
        malTopic: "MAL Top 10"
    };

    // 閫氱敤鐨勮幏鍙栨暟鎹嚱鏁?
    const fetchAnimeData = async (
        endpoint: string,
        cacheKey: string
    ): Promise<AnimeInfo[]> => {
        // 灏濊瘯浠庣紦瀛樿鍙?
        const cached = sessionStorage.getItem(cacheKey);
        if (cached) {
            try {
                const cachedItems = JSON.parse(cached) as AnimeInfo[];
                const dedupedCachedItems = dedupeAnimeList(cachedItems);
                if (dedupedCachedItems.length !== cachedItems.length) {
                    sessionStorage.setItem(cacheKey, JSON.stringify(dedupedCachedItems));
                }

                return dedupedCachedItems;
            } catch (e) {
                console.error(`Failed to parse cached data for ${cacheKey}:`, e);
                sessionStorage.removeItem(cacheKey);
            }
        }

        // 浠?API 鑾峰彇
        const response = await authFetch(`${API_BASE}${endpoint}`, {
            method: 'GET'
        });

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const result = await response.json();

        if (!result.success) {
            throw new Error(result.message);
        }

        // 淇濆瓨鍒扮紦瀛?
        const dedupedItems = dedupeAnimeList(result.data.animes ?? []);
        sessionStorage.setItem(cacheKey, JSON.stringify(dedupedItems));
        return dedupedItems;
    };

    useEffect(() => {
        const fetchAllData = async () => {
            try {
                setLoading(true);
                setError(null);

                // 骞惰鑾峰彇鎵€鏈夋暟鎹?(all endpoints need TMDB token for backdrop URLs)
                const [todayResult, bangumiResult, anilistResult, malResult] = await Promise.allSettled([
                    fetchAnimeData("/today", CACHE_KEYS.todayAnimes),
                    fetchAnimeData("/top/bangumi", CACHE_KEYS.bangumiTop10),
                    fetchAnimeData("/top/anilist", CACHE_KEYS.anilistTop10),
                    fetchAnimeData("/top/mal", CACHE_KEYS.malTop10)
                ]);

                const today = todayResult.status === "fulfilled" ? todayResult.value : [];
                const bangumi = bangumiResult.status === "fulfilled" ? bangumiResult.value : [];
                const anilist = anilistResult.status === "fulfilled" ? anilistResult.value : [];
                const mal = malResult.status === "fulfilled" ? malResult.value : [];

                const failures = [
                    todayResult.status === "rejected" ? `today: ${String(todayResult.reason)}` : null,
                    bangumiResult.status === "rejected" ? `bangumi: ${String(bangumiResult.reason)}` : null,
                    anilistResult.status === "rejected" ? `anilist: ${String(anilistResult.reason)}` : null,
                    malResult.status === "rejected" ? `mal: ${String(malResult.reason)}` : null
                ].filter((item): item is string => Boolean(item));

                setTodayAnimes(today);
                setBangumiTop10(bangumi);
                setAnilistTop10(anilist);
                setMalTop10(mal);

                if (failures.length > 0) {
                    console.warn("Some anime sources failed:", failures.join(" | "));
                }

                if (today.length === 0 && bangumi.length === 0 && anilist.length === 0 && mal.length === 0 && failures.length > 0) {
                    setError(language === "zh" ? "\u52a8\u6f2b\u6570\u636e\u6682\u65f6\u4e0d\u53ef\u7528\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5" : "Anime data is temporarily unavailable");
                }

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
