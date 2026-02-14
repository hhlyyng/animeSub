import { useCallback, useEffect, useMemo, useState } from "react";
import type { AnimeInfo } from "../../../types/anime";
import type { ManualDownloadAnimeItem, SubscriptionItem } from "../../../types/subscription";
import { useAppStore } from "../../../stores/useAppStores";
import { API_BASE_URL } from "../../../config/env";
import * as subscriptionApi from "../../../services/subscriptionApi";
import { LoadingSpinner } from "../../../components/common/LoadingSpinner";
import { ErrorMessage } from "../../../components/common/ErrorMessage";
import { SubscriptionDownloadDetailModal } from "./SubscriptionDownloadDetailModal";
import { SubscriptionInfo } from "./SubscriptionInfo";

type SubscriptionCardItem = {
  subscription: SubscriptionItem;
  anime: AnimeInfo;
};

type ManualAnimeCardItem = {
  manual: ManualDownloadAnimeItem;
  anime: AnimeInfo;
};

type SelectedCardItem =
  | { kind: "subscribed"; item: SubscriptionCardItem }
  | { kind: "manual"; item: ManualAnimeCardItem };

const CATALOG_CACHE_KEY = "v1:subscription-page:anime-catalog";
const ANIME_API_BASE = `${API_BASE_URL}/anime`;

const SUBSCRIPTION_PAGE_TEXT = {
  zh: {
    subscribedTopic: "\u5df2\u8ba2\u9605\u52a8\u6f2b",
    manualTopic: "\u672a\u8ba2\u9605/\u5df2\u4e0b\u8f7d",
    noSubscribed: "\u6682\u65e0\u5df2\u8ba2\u9605\u52a8\u6f2b\u6570\u636e",
    noManual: "\u6682\u65e0\u672a\u8ba2\u9605/\u5df2\u4e0b\u8f7d\u52a8\u6f2b\u6570\u636e",
  },
  en: {
    subscribedTopic: "Subscribed Anime",
    manualTopic: "Unsubscribed / Downloaded",
    noSubscribed: "No subscribed anime data",
    noManual: "No unsubscribed/downloaded anime data",
  },
} as const;

function resolveAnimeIdentity(anime: AnimeInfo): string {
  return (
    anime.external_urls?.mal?.trim() ||
    anime.external_urls?.anilist?.trim() ||
    anime.external_urls?.bangumi?.trim() ||
    anime.bangumi_id?.trim() ||
    `${anime.jp_title}|${anime.en_title}|${anime.ch_title ?? ""}`
  );
}

function dedupeAnimeList(animes: AnimeInfo[]): AnimeInfo[] {
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
}

function normalizeAnimeData(
  bangumiId: number,
  title: string,
  mikanBangumiId: string | null | undefined,
  sourceAnime: AnimeInfo | undefined
): AnimeInfo {
  if (sourceAnime) {
    return {
      ...sourceAnime,
      bangumi_id: sourceAnime.bangumi_id || String(bangumiId),
      mikan_bangumi_id: mikanBangumiId || sourceAnime.mikan_bangumi_id,
      images: {
        portrait: sourceAnime.images?.portrait || "",
        landscape: sourceAnime.images?.landscape || sourceAnime.images?.portrait || "",
      },
    };
  }

  return {
    bangumi_id: String(bangumiId),
    mikan_bangumi_id: mikanBangumiId || undefined,
    jp_title: title,
    ch_title: title,
    en_title: title,
    score: "--",
    images: {
      portrait: "",
      landscape: "",
    },
  };
}

async function fetchAnimeEndpoint(endpoint: string): Promise<AnimeInfo[]> {
  const response = await fetch(`${ANIME_API_BASE}${endpoint}`, { method: "GET" });
  if (!response.ok) {
    throw new Error(`Failed to load ${endpoint}: ${response.status}`);
  }

  const payload = (await response.json()) as {
    success?: boolean;
    data?: { animes?: AnimeInfo[] };
    message?: string;
  };

  if (!payload.success) {
    throw new Error(payload.message || `Failed to load ${endpoint}`);
  }

  return payload.data?.animes ?? [];
}

async function loadAnimeCatalog(): Promise<AnimeInfo[]> {
  const cached = sessionStorage.getItem(CATALOG_CACHE_KEY);
  if (cached) {
    try {
      const parsed = JSON.parse(cached) as AnimeInfo[];
      return dedupeAnimeList(parsed);
    } catch {
      sessionStorage.removeItem(CATALOG_CACHE_KEY);
    }
  }

  const settled = await Promise.allSettled([
    fetchAnimeEndpoint("/today"),
    fetchAnimeEndpoint("/top/bangumi"),
    fetchAnimeEndpoint("/top/anilist"),
    fetchAnimeEndpoint("/top/mal"),
  ]);

  const merged: AnimeInfo[] = [];
  for (const result of settled) {
    if (result.status === "fulfilled") {
      merged.push(...result.value);
    }
  }

  if (merged.length === 0) {
    const rejected = settled.find((item): item is PromiseRejectedResult => item.status === "rejected");
    throw (rejected?.reason instanceof Error ? rejected.reason : new Error("Failed to load anime catalog"));
  }

  const deduped = dedupeAnimeList(merged);
  sessionStorage.setItem(CATALOG_CACHE_KEY, JSON.stringify(deduped));
  return deduped;
}

function resolveAnimeByBangumi(catalog: AnimeInfo[]): Map<string, AnimeInfo> {
  const animeByBangumiId = new Map<string, AnimeInfo>();
  for (const anime of catalog) {
    const key = anime.bangumi_id?.trim();
    if (!key || animeByBangumiId.has(key)) {
      continue;
    }
    animeByBangumiId.set(key, anime);
  }
  return animeByBangumiId;
}

export function MySubscriptionDownloadPage() {
  const language = useAppStore((state) => state.language);
  const setGlobalModalOpen = useAppStore((state) => state.setModalOpen);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [subscribedItems, setSubscribedItems] = useState<SubscriptionCardItem[]>([]);
  const [manualItems, setManualItems] = useState<ManualAnimeCardItem[]>([]);
  const [selected, setSelected] = useState<SelectedCardItem | null>(null);

  const text = SUBSCRIPTION_PAGE_TEXT[language];
  const subscribedAnimeItems = useMemo(() => subscribedItems.map((item) => item.anime), [subscribedItems]);
  const manualAnimeItems = useMemo(() => manualItems.map((item) => item.anime), [manualItems]);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        setLoading(true);
        setError(null);

        const [subscriptions, manualAnimes, catalog] = await Promise.all([
          subscriptionApi.getSubscriptions(),
          subscriptionApi.getManualDownloadAnimes(),
          loadAnimeCatalog(),
        ]);

        if (cancelled) {
          return;
        }

        const animeByBangumiId = resolveAnimeByBangumi(catalog);

        const mappedSubscriptions = subscriptions
          .slice()
          .sort((a, b) => {
            if (a.isEnabled !== b.isEnabled) {
              return a.isEnabled ? -1 : 1;
            }
            return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
          })
          .map((subscription) => {
            const matchedAnime = animeByBangumiId.get(String(subscription.bangumiId));
            return {
              subscription,
              anime: normalizeAnimeData(
                subscription.bangumiId,
                subscription.title,
                subscription.mikanBangumiId,
                matchedAnime
              ),
            };
          });

        const mappedManual = manualAnimes
          .slice()
          .sort((a, b) => new Date(b.lastTaskAt).getTime() - new Date(a.lastTaskAt).getTime())
          .map((manual) => {
            const matchedAnime = animeByBangumiId.get(String(manual.bangumiId));
            return {
              manual,
              anime: normalizeAnimeData(manual.bangumiId, manual.title, manual.mikanBangumiId, matchedAnime),
            };
          });

        setSubscribedItems(mappedSubscriptions);
        setManualItems(mappedManual);
      } catch (err) {
        if (cancelled) {
          return;
        }
        setError(err instanceof Error ? err.message : "Failed to load subscriptions");
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void load();

    return () => {
      cancelled = true;
    };
  }, []);

  const handleSelectSubscribed = useCallback(
    (anime: AnimeInfo) => {
      const matched =
        subscribedItems.find(
          (item) =>
            item.anime.bangumi_id === anime.bangumi_id &&
            item.anime.mikan_bangumi_id === anime.mikan_bangumi_id
        ) ?? subscribedItems.find((item) => item.anime.bangumi_id === anime.bangumi_id);

      if (!matched) {
        return;
      }

      setSelected({ kind: "subscribed", item: matched });
      setGlobalModalOpen(true);
    },
    [setGlobalModalOpen, subscribedItems]
  );

  const handleSelectManual = useCallback(
    (anime: AnimeInfo) => {
      const bangumiId = Number.parseInt(anime.bangumi_id, 10);
      if (!Number.isFinite(bangumiId) || bangumiId <= 0) {
        return;
      }

      const matched = manualItems.find((item) => item.manual.bangumiId === bangumiId);
      if (!matched) {
        return;
      }

      setSelected({ kind: "manual", item: matched });
      setGlobalModalOpen(true);
    },
    [manualItems, setGlobalModalOpen]
  );

  const handleCloseModal = useCallback(() => {
    setSelected(null);
    setGlobalModalOpen(false);
  }, [setGlobalModalOpen]);

  return (
    <>
      <section className="content-header w-full pb-10">
        {loading && <LoadingSpinner />}
        {error && <ErrorMessage message={error} />}

        {!loading && !error && (
          <div className="flex w-full flex-col gap-8">
            <SubscriptionInfo
              topic={text.subscribedTopic}
              items={subscribedAnimeItems}
              language={language}
              onSelect={handleSelectSubscribed}
              emptyText={text.noSubscribed}
            />
            <SubscriptionInfo
              topic={text.manualTopic}
              items={manualAnimeItems}
              language={language}
              onSelect={handleSelectManual}
              emptyText={text.noManual}
            />
          </div>
        )}
      </section>

      {selected && (
        <SubscriptionDownloadDetailModal
          open={selected !== null}
          anime={selected.item.anime}
          subscription={selected.kind === "subscribed" ? selected.item.subscription : undefined}
          manualBangumiId={selected.kind === "manual" ? selected.item.manual.bangumiId : undefined}
          onClose={handleCloseModal}
        />
      )}
    </>
  );
}

export default MySubscriptionDownloadPage;
