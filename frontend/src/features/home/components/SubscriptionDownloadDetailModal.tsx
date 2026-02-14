import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { AnimeInfo } from "../../../types/anime";
import type { TorrentInfo } from "../../../types/mikan";
import type { SubscriptionDownloadHistoryItem, SubscriptionItem } from "../../../types/subscription";
import { useAppStore } from "../../../stores/useAppStores";
import * as mikanApi from "../../../services/mikanApi";
import * as subscriptionApi from "../../../services/subscriptionApi";
import { formatFileSize } from "../../../utils/formatFileSize";
import { LoadingSpinner } from "../../../components/common/LoadingSpinner";
import { ErrorMessage } from "../../../components/common/ErrorMessage";
import { CloseIcon } from "../../../components/icons/CloseIcon";

type SubscriptionDownloadDetailModalProps = {
  open: boolean;
  anime: AnimeInfo;
  subscription?: SubscriptionItem;
  manualBangumiId?: number;
  onClose: () => void;
};

type TaskVisualStatus = "success" | "downloading" | "queued" | "error";
type TaskFilter = "all" | TaskVisualStatus;

type VisualTask = {
  key: string;
  hash: string;
  index: number;
  title: string;
  status: TaskVisualStatus;
  progress: number;
  speed: number;
  publishedAt: string;
  rawStatus: string;
  errorMessage?: string | null;
  fileSize?: number | null;
};

const HISTORY_LIMIT = 300;
const POLLING_INTERVAL_MS = 3000;
const HISTORY_REFRESH_MS = 10000;

function normalizeHash(hash: string | null | undefined): string {
  return hash?.trim().toUpperCase() ?? "";
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value)) return 0;
  if (value <= 1) return Math.round(Math.max(0, Math.min(100, value * 100)) * 100) / 100;
  return Math.round(Math.max(0, Math.min(100, value)) * 100) / 100;
}

function normalizeTorrentState(state?: string): string {
  return (state ?? "").toLowerCase();
}

function isTorrentCompletedState(state?: string, progress?: number): boolean {
  if (typeof progress === "number" && clampPercent(progress) >= 99.9) {
    return true;
  }
  const normalized = normalizeTorrentState(state);
  return (
    normalized.includes("completed") ||
    normalized.includes("uploading") ||
    normalized.includes("forcedup") ||
    normalized.includes("checkingup") ||
    normalized.includes("stalledup") ||
    normalized.includes("queuedup")
  );
}

function isTorrentDownloadingState(state?: string): boolean {
  const normalized = normalizeTorrentState(state);
  return (
    normalized.includes("downloading") ||
    normalized.includes("forceddl") ||
    normalized.includes("metadl") ||
    normalized.includes("allocating") ||
    normalized.includes("checkingdl")
  );
}

function isTorrentQueuedState(state?: string): boolean {
  const normalized = normalizeTorrentState(state);
  return (
    normalized.includes("paused") ||
    normalized.includes("stopped") ||
    normalized.includes("stop") ||
    normalized.includes("stalldl") ||
    normalized.includes("queueddl") ||
    normalized.includes("pending")
  );
}

function isTorrentErrorState(state?: string): boolean {
  const normalized = normalizeTorrentState(state);
  return normalized.includes("error") || normalized.includes("missingfiles");
}

function resolveHistoryStatus(status: string): TaskVisualStatus {
  const normalized = status.trim().toLowerCase();
  if (normalized.includes("completed") || normalized.includes("success")) {
    return "success";
  }
  if (normalized.includes("failed") || normalized.includes("error")) {
    return "error";
  }
  if (normalized.includes("downloading")) {
    return "downloading";
  }
  return "queued";
}

function resolveTaskStatus(task: SubscriptionDownloadHistoryItem, info: TorrentInfo | undefined): TaskVisualStatus {
  if (info) {
    if (isTorrentErrorState(info.state)) return "error";
    if (isTorrentCompletedState(info.state, info.progress)) return "success";
    if (isTorrentDownloadingState(info.state)) return "downloading";
    if (isTorrentQueuedState(info.state)) return "queued";
  }

  if (typeof task.progress === "number") {
    const normalizedProgress = clampPercent(task.progress);
    if (normalizedProgress >= 99.9) {
      return "success";
    }
    if (normalizedProgress > 0 && resolveHistoryStatus(task.status) === "queued") {
      return "downloading";
    }
  }

  if (typeof task.downloadSpeed === "number" && task.downloadSpeed > 0) {
    return "downloading";
  }

  return resolveHistoryStatus(task.status);
}

function resolveTaskProgress(
  task: SubscriptionDownloadHistoryItem,
  status: TaskVisualStatus,
  info: TorrentInfo | undefined
): number {
  if (status === "success") return 100;
  if (!info) {
    if (typeof task.progress === "number") {
      return clampPercent(task.progress);
    }
    return status === "downloading" ? 1 : 0;
  }
  return clampPercent(info.progress);
}

function resolveTaskSpeed(
  task: SubscriptionDownloadHistoryItem,
  status: TaskVisualStatus,
  info: TorrentInfo | undefined
): number {
  if (status !== "downloading") return 0;
  if (info && typeof info.downloadSpeed === "number") {
    return Math.max(0, info.downloadSpeed);
  }
  return Math.max(0, task.downloadSpeed ?? 0);
}

function formatSpeed(bytesPerSecond: number, language: "zh" | "en"): string {
  if (bytesPerSecond <= 0) {
    return language === "zh" ? "0 B/\u79d2" : "0 B/s";
  }
  return `${formatFileSize(bytesPerSecond)}/${language === "zh" ? "\u79d2" : "s"}`;
}

function formatDateTime(value: string | null | undefined): string | null {
  if (!value) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toLocaleString();
}

function TaskStatusIcon({
  status,
  className,
}: {
  status: TaskVisualStatus;
  className?: string;
}) {
  if (status === "success") {
    return (
      <svg viewBox="0 0 24 24" fill="none" className={className} stroke="currentColor" strokeWidth={2.6}>
        <path d="M5 13l4 4L19 7" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }

  if (status === "error") {
    return (
      <svg viewBox="0 0 24 24" fill="none" className={className} stroke="currentColor" strokeWidth={2.2}>
        <path
          d="M4 12a8 8 0 0 1 13.66-5.66M20 12a8 8 0 0 1-13.66 5.66M18 4v4h-4M6 20v-4h4"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    );
  }

  return null;
}

function TaskSquare({
  task,
  language,
}: {
  task: VisualTask;
  language: "zh" | "en";
}) {
  if (task.status === "success") {
    return (
      <article
        title={task.title}
        className="relative aspect-square overflow-hidden rounded-xl border border-blue-700 bg-blue-700 p-2 text-white shadow-sm transition-transform duration-200 hover:-translate-y-0.5"
      >
        <div className="absolute right-1.5 top-1.5 rounded-full bg-white/20 p-0.5">
          <TaskStatusIcon status={task.status} className="h-3 w-3" />
        </div>
        <div className="flex h-full items-center justify-center">
          <span className="text-lg font-bold">{task.index + 1}</span>
        </div>
      </article>
    );
  }

  if (task.status === "downloading") {
    return (
      <article
        title={task.title}
        className="relative aspect-square overflow-hidden rounded-xl border border-blue-400 bg-white p-2 text-blue-900 shadow-sm transition-transform duration-200 hover:-translate-y-0.5"
      >
        <div
          className="absolute bottom-0 left-0 h-[30%] bg-blue-400/35 transition-[width] duration-500"
          style={{ width: `${task.progress}%` }}
          aria-hidden="true"
        />
        <div className="relative z-10 flex h-full flex-col items-center justify-center text-center">
          <span className="text-lg font-bold">{Math.round(task.progress)}%</span>
          <span className="mt-1 text-[11px] text-blue-700">{formatSpeed(task.speed, language)}</span>
        </div>
      </article>
    );
  }

  if (task.status === "error") {
    return (
      <article
        title={`${task.title}${task.errorMessage ? `\n${task.errorMessage}` : ""}`}
        className="relative aspect-square overflow-hidden rounded-xl border border-red-400 bg-red-50 p-2 text-red-700 shadow-sm transition-transform duration-200 hover:-translate-y-0.5"
      >
        <div className="flex h-full flex-col items-center justify-center gap-1.5">
          <TaskStatusIcon status={task.status} className="h-5 w-5 animate-spin" />
          <span className="text-xs font-semibold">{task.index + 1}</span>
        </div>
      </article>
    );
  }

  return (
    <article
      title={task.title}
      className="relative aspect-square overflow-hidden rounded-xl border border-dashed border-gray-400 bg-gray-100/65 p-2 text-gray-500 shadow-sm transition-transform duration-200 hover:-translate-y-0.5"
    >
      <div className="flex h-full items-center justify-center">
        <span className="text-base font-semibold">{task.index + 1}</span>
      </div>
    </article>
  );
}

export function SubscriptionDownloadDetailModal({
  open,
  anime,
  subscription,
  manualBangumiId,
  onClose,
}: SubscriptionDownloadDetailModalProps) {
  const language = useAppStore((state) => state.language);
  const isManualMode = !subscription && typeof manualBangumiId === "number" && manualBangumiId > 0;
  const [historyItems, setHistoryItems] = useState<SubscriptionDownloadHistoryItem[]>([]);
  const [torrentInfo, setTorrentInfo] = useState<Map<string, TorrentInfo>>(new Map());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<TaskFilter>("all");

  const stopPollingRef = useRef<(() => void) | null>(null);
  const historyTimerRef = useRef<number | null>(null);
  const latestRequestIdRef = useRef(0);

  const texts = useMemo(
    () =>
      language === "zh"
        ? {
            title: isManualMode ? "\u624b\u52a8\u4efb\u52a1\u603b\u89c8" : "\u8ba2\u9605\u4efb\u52a1\u603b\u89c8",
            overallProgress: "\u603b\u4f53\u8fdb\u5ea6",
            totalSpeed: "\u603b\u901f\u5ea6",
            taskCount: "\u4efb\u52a1\u6570",
            doneCount: "\u5df2\u5b8c\u6210",
            downloadingCount: "\u4e0b\u8f7d\u4e2d",
            queuedCount: "\u961f\u5217\u4e2d",
            errorCount: "\u5f02\u5e38",
            latestUpdate: "\u6700\u8fd1\u66f4\u65b0",
            noTask: isManualMode ? "\u8be5\u52a8\u6f2b\u6682\u65e0\u624b\u52a8\u4efb\u52a1" : "\u8be5\u8ba2\u9605\u6682\u65e0\u4e0b\u8f7d\u4efb\u52a1",
            all: "\u5168\u90e8",
          }
        : {
            title: isManualMode ? "Manual Task Overview" : "Subscription Task Overview",
            overallProgress: "Overall Progress",
            totalSpeed: "Total Speed",
            taskCount: "Tasks",
            doneCount: "Completed",
            downloadingCount: "Downloading",
            queuedCount: "Queued",
            errorCount: "Error",
            latestUpdate: "Latest Update",
            noTask: isManualMode ? "No manual tasks found for this anime" : "No tasks found for this subscription",
            all: "All",
          },
    [isManualMode, language]
  );

  const primaryTitle =
    language === "zh"
      ? anime.ch_title || anime.en_title || anime.jp_title || subscription?.title || "Unknown"
      : anime.en_title || anime.ch_title || anime.jp_title || subscription?.title || "Unknown";
  const secondaryTitle = anime.jp_title !== primaryTitle ? anime.jp_title : null;

  useEffect(() => {
    if (!open) return;
    if (!subscription && !isManualMode) {
      setError(language === "zh" ? "\u52a0\u8f7d\u4efb\u52a1\u53c2\u6570\u7f3a\u5931" : "Missing task scope");
      return;
    }

    setFilter("all");
    setError(null);
    setHistoryItems([]);
    setTorrentInfo(new Map());

    const requestId = latestRequestIdRef.current + 1;
    latestRequestIdRef.current = requestId;
    let cancelled = false;

    const loadHistory = async () => {
      try {
        setLoading(true);
        const items = subscription
          ? await subscriptionApi.getSubscriptionHistory(subscription.id, HISTORY_LIMIT)
          : await subscriptionApi.getManualAnimeHistory(manualBangumiId!, HISTORY_LIMIT);
        if (cancelled || requestId !== latestRequestIdRef.current) {
          return;
        }
        setHistoryItems(items);
        setError(null);
      } catch (err) {
        if (cancelled || requestId !== latestRequestIdRef.current) {
          return;
        }
        setError(err instanceof Error ? err.message : "Failed to load task history");
      } finally {
        if (!cancelled && requestId === latestRequestIdRef.current) {
          setLoading(false);
        }
      }
    };

    void loadHistory();

    stopPollingRef.current?.();
    stopPollingRef.current = mikanApi.startProgressPolling(POLLING_INTERVAL_MS, (progresses) => {
      if (cancelled || requestId !== latestRequestIdRef.current) {
        return;
      }
      setTorrentInfo(progresses);
    });

    if (historyTimerRef.current !== null) {
      window.clearInterval(historyTimerRef.current);
      historyTimerRef.current = null;
    }
    historyTimerRef.current = window.setInterval(() => {
      void loadHistory();
    }, HISTORY_REFRESH_MS);

    return () => {
      cancelled = true;
      stopPollingRef.current?.();
      stopPollingRef.current = null;
      if (historyTimerRef.current !== null) {
        window.clearInterval(historyTimerRef.current);
        historyTimerRef.current = null;
      }
    };
  }, [open, subscription, manualBangumiId, isManualMode, language]);

  useEffect(() => {
    if (!open) return;

    const handleEsc = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    document.addEventListener("keydown", handleEsc);
    document.body.style.overflow = "hidden";

    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "unset";
    };
  }, [open, onClose]);

  const tasks = useMemo<VisualTask[]>(() => {
    return historyItems.map((item, index) => {
      const normalizedHash = normalizeHash(item.torrentHash);
      const info = normalizedHash ? torrentInfo.get(normalizedHash) : undefined;
      const status = resolveTaskStatus(item, info);
      const progress = resolveTaskProgress(item, status, info);
      const speed = resolveTaskSpeed(item, status, info);
      return {
        key: normalizedHash || `${item.id}-${index}`,
        hash: normalizedHash,
        index,
        title: item.title,
        status,
        progress,
        speed,
        publishedAt: item.publishedAt,
        rawStatus: item.status,
        errorMessage: item.errorMessage,
        fileSize: item.fileSize,
      };
    });
  }, [historyItems, torrentInfo]);

  const statusCounts = useMemo(() => {
    const initial = {
      success: 0,
      downloading: 0,
      queued: 0,
      error: 0,
    };

    for (const task of tasks) {
      initial[task.status] += 1;
    }

    return initial;
  }, [tasks]);

  const filteredTasks = useMemo(() => {
    if (filter === "all") return tasks;
    return tasks.filter((task) => task.status === filter);
  }, [tasks, filter]);

  const totalDownloadSpeed = useMemo(() => {
    return tasks.reduce((sum, task) => (task.status === "downloading" ? sum + task.speed : sum), 0);
  }, [tasks]);

  const overallProgress = useMemo(() => {
    if (tasks.length === 0) return 0;

    let weightedSum = 0;
    let totalWeight = 0;
    for (const task of tasks) {
      const weight = typeof task.fileSize === "number" && task.fileSize > 0 ? task.fileSize : 1;
      weightedSum += task.progress * weight;
      totalWeight += weight;
    }

    if (totalWeight <= 0) return 0;
    return Math.round((weightedSum / totalWeight) * 100) / 100;
  }, [tasks]);

  const latestPublishedAt = useMemo(() => {
    if (historyItems.length === 0) return null;
    return formatDateTime(historyItems[0]?.publishedAt);
  }, [historyItems]);

  if (!open) return null;

  const filters: Array<{ key: TaskFilter; label: string; count: number }> = [
    { key: "all", label: texts.all, count: tasks.length },
    { key: "downloading", label: texts.downloadingCount, count: statusCounts.downloading },
    { key: "success", label: texts.doneCount, count: statusCounts.success },
    { key: "queued", label: texts.queuedCount, count: statusCounts.queued },
    { key: "error", label: texts.errorCount, count: statusCounts.error },
  ];

  const modalContent = (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" onClick={onClose}>
      <div className="absolute inset-0 bg-black/55" />

      <div
        className="relative z-10 w-full max-w-6xl overflow-hidden rounded-2xl bg-white shadow-2xl"
        onClick={(event) => event.stopPropagation()}
      >
        <button
          type="button"
          onClick={onClose}
          className="absolute right-4 top-4 z-20 rounded-md bg-white/90 p-1 text-gray-700 shadow-sm transition-colors hover:bg-gray-100"
          aria-label="Close"
        >
          <CloseIcon />
        </button>

        <div className="max-h-[90vh] overflow-y-auto">
          <section className="grid gap-6 border-b border-gray-200 p-6 md:grid-cols-[220px_1fr]">
            <div className="w-full">
              <img
                src={anime.images?.portrait || ""}
                alt={primaryTitle}
                className="h-auto w-full rounded-xl object-cover shadow-md"
              />
            </div>

            <div className="min-w-0">
              <h2 className="pr-12 text-2xl font-bold text-gray-900">{primaryTitle}</h2>
              {secondaryTitle && <p className="mt-1 text-sm text-gray-500">{secondaryTitle}</p>}
              <p className="mt-3 text-sm text-gray-600">{texts.title}</p>

              <div className="mt-6">
                <div className="mb-2 flex items-center justify-between text-sm text-gray-700">
                  <span>{texts.overallProgress}</span>
                  <span className="font-semibold text-gray-900">{Math.round(overallProgress)}%</span>
                </div>
                <div className="h-3 overflow-hidden rounded-full bg-gray-200">
                  <div
                    className="h-full bg-blue-600 transition-[width] duration-500"
                    style={{ width: `${overallProgress}%` }}
                    aria-hidden="true"
                  />
                </div>
              </div>

              <div className="mt-5 grid grid-cols-2 gap-3 md:grid-cols-3">
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">{texts.totalSpeed}</p>
                  <p className="mt-1 text-base font-semibold text-gray-900">{formatSpeed(totalDownloadSpeed, language)}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">{texts.taskCount}</p>
                  <p className="mt-1 text-base font-semibold text-gray-900">{tasks.length}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">{texts.doneCount}</p>
                  <p className="mt-1 text-base font-semibold text-gray-900">{statusCounts.success}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">{texts.downloadingCount}</p>
                  <p className="mt-1 text-base font-semibold text-gray-900">{statusCounts.downloading}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">{texts.queuedCount}</p>
                  <p className="mt-1 text-base font-semibold text-gray-900">{statusCounts.queued}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">{texts.errorCount}</p>
                  <p className="mt-1 text-base font-semibold text-gray-900">{statusCounts.error}</p>
                </div>
              </div>

              {latestPublishedAt && (
                <p className="mt-4 text-xs text-gray-500">
                  {texts.latestUpdate}: {latestPublishedAt}
                </p>
              )}
            </div>
          </section>

          <section className="bg-gray-50 p-6">
            {loading && <LoadingSpinner />}
            {error && <ErrorMessage message={error} />}

            {!loading && !error && (
              <>
                <div className="mb-4 flex flex-wrap gap-2">
                  {filters.map((item) => (
                    <button
                      key={item.key}
                      type="button"
                      onClick={() => setFilter(item.key)}
                      className={`rounded-full border px-3 py-1.5 text-xs transition-colors ${
                        filter === item.key
                          ? "border-blue-600 bg-blue-600 text-white"
                          : "border-gray-300 bg-white text-gray-700 hover:bg-gray-100"
                      }`}
                    >
                      {item.label} ({item.count})
                    </button>
                  ))}
                </div>

                {filteredTasks.length === 0 ? (
                  <p className="text-sm text-gray-500">{texts.noTask}</p>
                ) : (
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
                    {filteredTasks.map((task) => (
                      <TaskSquare key={task.key} task={task} language={language} />
                    ))}
                  </div>
                )}
              </>
            )}
          </section>
        </div>
      </div>
    </div>
  );

  return createPortal(modalContent, document.body);
}

export default SubscriptionDownloadDetailModal;
