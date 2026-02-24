import { useState } from "react";
import AnimeFlow from "./AnimeInfoFlow";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";
import { API_BASE_URL } from "../../../config/env";
import { authFetch } from "../../../services/apiClient";

const API_BASE = `${API_BASE_URL}/anime`;

// Mirror AnimeInfoFlow's layout constants so the search header aligns with the results title.
const CARD_BASE_WIDTH = 200;
const FLOW_GAP = 16;
const NAV_BUTTON_SIZE = 48;
const TITLE_OFFSET = NAV_BUTTON_SIZE + FLOW_GAP; // 64px — same as AnimeInfoFlow TITLE_BASE_OFFSET
const DESKTOP_WINDOW_SIZE = 6;

function calcFlowBaseWidth(itemCount: number): number {
    const visible = Math.max(1, Math.min(DESKTOP_WINDOW_SIZE, itemCount));
    const track = visible * CARD_BASE_WIDTH + Math.max(0, visible - 1) * FLOW_GAP;
    return track + NAV_BUTTON_SIZE * 2 + FLOW_GAP * 2;
}

const SearchPage = () => {
    const [query, setQuery] = useState("");
    const [results, setResults] = useState<AnimeInfo[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [searched, setSearched] = useState(false);
    const language = useAppStore((state) => state.language);

    const texts = {
        placeholder: language === 'zh' ? '键入动漫名称以搜索...' : 'Type an anime title to search...',
        searchBtn: language === 'zh' ? '搜索' : 'Search',
        loading: language === 'zh' ? '搜索中...' : 'Searching...',
        noResults: language === 'zh' ? '未找到相关动漫' : 'No anime found',
        error: language === 'zh' ? '搜索失败' : 'Search failed',
        topic: language === 'zh' ? '搜索结果' : 'Results',
    };

    const doSearch = async () => {
        const q = query.trim();
        if (!q) return;

        setLoading(true);
        setError(null);
        setSearched(true);

        try {
            const response = await authFetch(`${API_BASE}/search?q=${encodeURIComponent(q)}`);
            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
            const result = await response.json();
            if (!result.success) throw new Error(result.message);
            setResults(result.data.animes ?? []);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Unknown error');
        } finally {
            setLoading(false);
        }
    };

    const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
        if (e.key === 'Enter') doSearch();
    };

    // Always use the desktop window-size reference (6 cards) so the "搜索" header
    // aligns with every other AnimeFlow section title on the page.
    const flowBaseWidth = calcFlowBaseWidth(DESKTOP_WINDOW_SIZE);
    const sectionLeft = `calc(50% - ${flowBaseWidth / 2}px)`;
    const itemLeft = `${TITLE_OFFSET}px`;

    return (
        <div className="content-header w-full">
            <div className="anime-flow-container">
                <div className="w-full">
                    <div className="flex flex-col" style={{ marginLeft: sectionLeft }}>
                        <div className="flex gap-2 mb-6" style={{ marginLeft: itemLeft }}>
                            <input
                                type="text"
                                value={query}
                                onChange={(e) => setQuery(e.target.value)}
                                onKeyDown={handleKeyDown}
                                placeholder={texts.placeholder}
                                className="flex-1 max-w-md px-4 py-2 border border-gray-300 rounded-lg focus:outline-none"
                            />
                            <button
                                onClick={doSearch}
                                disabled={loading}
                                className="px-5 py-2 border border-gray-300 rounded-lg text-gray-900 hover:bg-gray-100 disabled:opacity-50 transition-colors"
                            >
                                {texts.searchBtn}
                            </button>
                        </div>
                        {loading && (
                            <div className="text-gray-500" style={{ marginLeft: itemLeft }}>{texts.loading}</div>
                        )}
                        {error && (
                            <div className="text-red-500" style={{ marginLeft: itemLeft }}>{texts.error}: {error}</div>
                        )}
                        {!loading && searched && results.length === 0 && !error && (
                            <div className="text-gray-500" style={{ marginLeft: itemLeft }}>{texts.noResults}</div>
                        )}
                    </div>
                    {results.length > 0 && (
                        <AnimeFlow topic={texts.topic} items={results} disableExpand />
                    )}
                </div>
            </div>
        </div>
    );
};

export default SearchPage;
