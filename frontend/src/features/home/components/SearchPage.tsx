import { useState } from "react";
import AnimeFlow from "./AnimeInfoFlow";
import type { AnimeInfo } from "../../../types/anime";
import { useAppStore } from "../../../stores/useAppStores";
import { API_BASE_URL } from "../../../config/env";

const API_BASE = `${API_BASE_URL}/anime`;

const SearchPage = () => {
    const [query, setQuery] = useState("");
    const [results, setResults] = useState<AnimeInfo[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [searched, setSearched] = useState(false);
    const language = useAppStore((state) => state.language);

    const texts = {
        placeholder: language === 'zh' ? '输入动漫名称...' : 'Enter anime title...',
        searchBtn: language === 'zh' ? '搜索' : 'Search',
        loading: language === 'zh' ? '搜索中...' : 'Searching...',
        noResults: language === 'zh' ? '未找到相关动漫' : 'No anime found',
        error: language === 'zh' ? '搜索失败' : 'Search failed',
        topic: language === 'zh' ? '搜索结果' : 'Results',
        title: language === 'zh' ? '搜索' : 'Search',
    };

    const doSearch = async () => {
        const q = query.trim();
        if (!q) return;

        setLoading(true);
        setError(null);
        setSearched(true);

        try {
            const response = await fetch(`${API_BASE}/search?q=${encodeURIComponent(q)}`);
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

    return (
        <div className="content-header w-full">
            <div className="anime-flow-container">
                <h2 className="text-2xl font-bold text-gray-900 mb-4 ml-16">{texts.title}</h2>
                <div className="flex gap-2 ml-16 mb-6">
                    <input
                        type="text"
                        value={query}
                        onChange={(e) => setQuery(e.target.value)}
                        onKeyDown={handleKeyDown}
                        placeholder={texts.placeholder}
                        className="flex-1 max-w-md px-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-400"
                    />
                    <button
                        onClick={doSearch}
                        disabled={loading}
                        className="px-5 py-2 bg-blue-500 text-white rounded-lg hover:bg-blue-600 disabled:opacity-50 transition-colors"
                    >
                        {texts.searchBtn}
                    </button>
                </div>
                {loading && (
                    <div className="ml-16 text-gray-500">{texts.loading}</div>
                )}
                {error && (
                    <div className="ml-16 text-red-500">{texts.error}: {error}</div>
                )}
                {!loading && searched && results.length === 0 && !error && (
                    <div className="ml-16 text-gray-500">{texts.noResults}</div>
                )}
            </div>
            {results.length > 0 && (
                <div className="anime-flow-container">
                    <AnimeFlow topic={texts.topic} items={results} />
                </div>
            )}
        </div>
    );
};

export default SearchPage;
