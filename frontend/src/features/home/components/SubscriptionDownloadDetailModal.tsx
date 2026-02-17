import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { AnimeInfo } from "../../../types/anime";
import type { TorrentInfo } from "../../../types/mikan";
import type { SubscriptionItem, SubscriptionTaskHash } from "../../../types/subscription";
import { useAppStore } from "../../../stores/useAppStores";
import * as mikanApi from "../../../services/mikanApi";
import * as subscriptionApi from "../../../services/subscriptionApi";
import { formatFileSize } from "../../../utils/formatFileSize";
import {
  normalizeHash,
  clampPercent,
  isTorrentCompletedState,
  isTorrentDownloadingState,
  isTorrentQueuedState,
  isTorrentErrorState,
} from "../../../utils/torrentState";
import { LoadingSpinner } from "../../../components/common/LoadingSpinner";
import { ErrorMessage } from "../../../components/common/ErrorMessage";
import toast from "react-hot-toast";
import { CloseIcon } from "../../../components/icons/CloseIcon";
import { CheckIcon } from "../../../components/icons/CheckIcon";
import DownloadIcon from "../../../components/icons/DownloadIcon";
import type { CancelSubscriptionAction } from "../../../services/subscriptionApi";
import * as subApi from "../../../services/subscriptionApi";

type SubscriptionDownloadDetailModalProps = {
  open: boolean;
  anime: AnimeInfo;
  subscription?: SubscriptionItem;
  manualBangumiId?: number;
  onClose: () => void;
  /** Called after a successful unsubscribe so the parent can update its list. */
  onUnsubscribed?: () => void;
};

type TaskVisualStatus = "success" | "downloading" | "queued" | "error";

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

const HASH_LIMIT = 300;
const POLLING_INTERVAL_MS = 3000;

/* ── Helpers ── */

function resolveTaskFromQb(
  taskHash: SubscriptionTaskHash,
  info: TorrentInfo | undefined
): { status: TaskVisualStatus; progress: number; speed: number; rawStatus: string; errorMessage?: string | null } {
  // Case 1: hash found in qBittorrent → use qBittorrent state (same logic as AnimeDetailModal)
  if (info) {
    if (isTorrentErrorState(info.state)) {
      return { status: "error", progress: clampPercent(info.progress), speed: 0, rawStatus: info.state ?? "" };
    }
    if (isTorrentCompletedState(info.state, info.progress)) {
      return { status: "success", progress: 100, speed: 0, rawStatus: info.state ?? "" };
    }
    if (isTorrentDownloadingState(info.state)) {
      return {
        status: "downloading",
        progress: clampPercent(info.progress),
        speed: Math.max(0, info.downloadSpeed ?? 0),
        rawStatus: info.state ?? "",
      };
    }
    if (isTorrentQueuedState(info.state)) {
      return { status: "queued", progress: clampPercent(info.progress), speed: 0, rawStatus: info.state ?? "" };
    }
    // Unknown qBittorrent state — treat as queued
    return { status: "queued", progress: clampPercent(info.progress), speed: 0, rawStatus: info.state ?? "" };
  }

  // Case 2: not in qBittorrent + DB says completed → show completed
  if (taskHash.isCompleted) {
    return { status: "success", progress: 100, speed: 0, rawStatus: "completed" };
  }

  // Case 3: not in qBittorrent + not completed → show queued
  return { status: "queued", progress: 0, speed: 0, rawStatus: "pending" };
}

function formatSpeed(bytesPerSecond: number, language: "zh" | "en"): string {
  if (bytesPerSecond <= 0) return language === "zh" ? "0 B/秒" : "0 B/s";
  return `${formatFileSize(bytesPerSecond)}/${language === "zh" ? "秒" : "s"}`;
}

function formatDateTime(value: string | null | undefined): string | null {
  if (!value) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toLocaleString();
}

/* ── Status visual config ── */
const STATUS_CONFIG: Record<TaskVisualStatus, { dot: string; ring: string; fill: string; barBg: string }> = {
  success:     { dot: "bg-emerald-500", ring: "ring-emerald-500/25", fill: "bg-emerald-500", barBg: "bg-emerald-100" },
  downloading: { dot: "bg-blue-500",    ring: "ring-blue-500/25",    fill: "bg-blue-500",    barBg: "bg-blue-100"    },
  queued:      { dot: "bg-gray-400",    ring: "ring-gray-400/20",    fill: "bg-gray-400",    barBg: "bg-gray-200"    },
  error:       { dot: "bg-red-500",     ring: "ring-red-500/25",     fill: "bg-red-500",     barBg: "bg-red-100"     },
};

const SECTION_ORDER: TaskVisualStatus[] = ["downloading", "queued", "error", "success"];

/* ── Chevron icon ── */
function ChevronIcon({ expanded }: { expanded: boolean }) {
  return (
    <svg
      viewBox="0 0 20 20"
      fill="currentColor"
      className={`h-4 w-4 text-gray-400 transition-transform duration-200 ${expanded ? "rotate-180" : ""}`}
    >
      <path fillRule="evenodd" d="M5.23 7.21a.75.75 0 011.06.02L10 11.168l3.71-3.938a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z" clipRule="evenodd" />
    </svg>
  );
}

/* ── Episode row ── */
function TaskRow({ task, language, animate }: { task: VisualTask; language: "zh" | "en"; animate: boolean }) {
  const cfg = STATUS_CONFIG[task.status];
  const episodeLabel = `EP ${String(task.index + 1).padStart(2, "0")}`;
  const sizeLabel = task.fileSize && task.fileSize > 0 ? formatFileSize(task.fileSize) : null;

  return (
    <div className="group flex items-center gap-3 px-3 py-2 transition-colors hover:bg-gray-50/80" title={task.title}>
      <span className={`h-2 w-2 flex-shrink-0 rounded-full ${cfg.dot} ${cfg.ring} ring-4`} />
      <span className="w-14 flex-shrink-0 text-sm font-semibold text-gray-800 tabular-nums">{episodeLabel}</span>

      <div className="flex-1 min-w-0">
        {task.status === "downloading" ? (
          <div className="flex items-center gap-2.5">
            <div className={`h-1.5 flex-1 rounded-full ${cfg.barBg} overflow-hidden`}>
              <div className={`h-full rounded-full ${cfg.fill} ${animate ? "transition-[width] duration-500" : ""}`} style={{ width: `${task.progress}%` }} />
            </div>
            <span className="flex-shrink-0 text-xs font-medium text-blue-600 tabular-nums w-10 text-right">{Math.round(task.progress)}%</span>
          </div>
        ) : task.status === "success" ? (
          <span className="text-xs text-emerald-600 font-medium">{language === "zh" ? "已完成" : "Completed"}</span>
        ) : task.status === "error" ? (
          <span className="text-xs text-red-500 font-medium truncate" title={task.errorMessage ?? undefined}>
            {task.errorMessage || (language === "zh" ? "异常" : "Error")}
          </span>
        ) : (
          <span className="text-xs text-gray-400">{language === "zh" ? "等待中" : "Queued"}</span>
        )}
      </div>

      {task.status === "downloading" && task.speed > 0 && (
        <span className="flex-shrink-0 text-[11px] text-blue-500 tabular-nums w-20 text-right">{formatSpeed(task.speed, language)}</span>
      )}
      {sizeLabel && (
        <span className="flex-shrink-0 text-[11px] text-gray-400 tabular-nums w-16 text-right hidden sm:block">{sizeLabel}</span>
      )}
    </div>
  );
}

/* ── Collapsible section ── */
function TaskSection({
  status,
  tasks,
  language,
  defaultExpanded,
  animate,
}: {
  status: TaskVisualStatus;
  tasks: VisualTask[];
  language: "zh" | "en";
  defaultExpanded: boolean;
  animate: boolean;
}) {
  const [expanded, setExpanded] = useState(defaultExpanded);
  const cfg = STATUS_CONFIG[status];

  const sectionLabels: Record<TaskVisualStatus, { zh: string; en: string }> = {
    downloading: { zh: "下载中", en: "Downloading" },
    success:     { zh: "已完成", en: "Completed" },
    queued:      { zh: "队列中", en: "Queued" },
    error:       { zh: "异常",   en: "Error" },
  };

  if (tasks.length === 0) return null;

  const label = language === "zh" ? sectionLabels[status].zh : sectionLabels[status].en;

  return (
    <div>
      <button
        type="button"
        onClick={() => setExpanded((prev) => !prev)}
        className="w-full flex items-center gap-2.5 px-4 py-2.5 hover:bg-gray-50 transition-colors"
      >
        <span className={`h-2 w-2 rounded-full ${cfg.dot}`} />
        <span className="text-sm font-medium text-gray-700">{label}</span>
        <span className="text-xs text-gray-400 tabular-nums">{tasks.length}</span>
        <span className="flex-1" />
        <ChevronIcon expanded={expanded} />
      </button>
      {expanded && (
        <div className="pb-1">
          {tasks.map((task) => (
            <TaskRow key={task.key} task={task} language={language} animate={animate} />
          ))}
        </div>
      )}
    </div>
  );
}

/* ── Main modal ── */
export function SubscriptionDownloadDetailModal({
  open,
  anime,
  subscription,
  manualBangumiId,
  onClose,
  onUnsubscribed,
}: SubscriptionDownloadDetailModalProps) {
  const language = useAppStore((state) => state.language);
  const isManualMode = !subscription && typeof manualBangumiId === "number" && manualBangumiId > 0;
  const [taskHashes, setTaskHashes] = useState<SubscriptionTaskHash[]>([]);
  const [torrentInfo, setTorrentInfo] = useState<Map<string, TorrentInfo>>(new Map());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showUnsubConfirm, setShowUnsubConfirm] = useState(false);
  const [unsubBusy, setUnsubBusy] = useState(false);

  // Wait for both hashes + first polling before showing content, to prevent blue→green flash
  const [readyToAnimate, setReadyToAnimate] = useState(false);
  const initialLoadDoneRef = useRef(false);
  const firstPollReceivedRef = useRef(false);

  const stopPollingRef = useRef<(() => void) | null>(null);
  const latestRequestIdRef = useRef(0);

  const texts = useMemo(
    () =>
      language === "zh"
        ? {
            title: isManualMode ? "手动任务" : "订阅任务",
            noTask: isManualMode ? "该动漫暂无手动任务" : "该订阅暂无下载任务",
            episodes: "集",
            latestUpdate: "最近更新",
            completed: "已完成",
          }
        : {
            title: isManualMode ? "Manual Tasks" : "Subscription Tasks",
            noTask: isManualMode ? "No manual tasks found" : "No download tasks found",
            episodes: "ep",
            latestUpdate: "Last updated",
            completed: "completed",
          },
    [isManualMode, language]
  );

  const primaryTitle =
    language === "zh"
      ? anime.ch_title || anime.en_title || anime.jp_title || subscription?.title || "Unknown"
      : anime.en_title || anime.ch_title || anime.jp_title || subscription?.title || "Unknown";
  const secondaryTitle = anime.jp_title !== primaryTitle ? anime.jp_title : null;

  const handleUnsubscribeAction = useCallback(async (action: CancelSubscriptionAction) => {
    if (!subscription || unsubBusy) return;
    try {
      setUnsubBusy(true);
      await subApi.cancelSubscription(subscription.id, action);
      toast.success(language === "zh" ? "已取消订阅" : "Unsubscribed");
      setShowUnsubConfirm(false);
      onUnsubscribed?.();
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Failed to unsubscribe";
      toast.error(language === "zh" ? `取消订阅失败: ${msg}` : `Unsubscribe failed: ${msg}`);
    } finally {
      setUnsubBusy(false);
    }
  }, [subscription, unsubBusy, language, onUnsubscribed]);

  // One-shot hash loader
  const loadHashes = useCallback(async (requestId: number) => {
    try {
      const items = subscription
        ? await subscriptionApi.getSubscriptionTaskHashes(subscription.id, HASH_LIMIT)
        : await subscriptionApi.getManualAnimeTaskHashes(manualBangumiId!, HASH_LIMIT);
      if (requestId !== latestRequestIdRef.current) return;
      setTaskHashes(items);
      setError(null);
    } catch (err) {
      if (requestId !== latestRequestIdRef.current) return;
      setError(err instanceof Error ? err.message : "Failed to load task hashes");
    }
  }, [subscription, manualBangumiId]);

  useEffect(() => {
    if (!open) {
      initialLoadDoneRef.current = false;
      setShowUnsubConfirm(false);
      setUnsubBusy(false);
      return;
    }
    if (!subscription && !isManualMode) {
      setError(language === "zh" ? "加载任务参数缺失" : "Missing task scope");
      return;
    }

    setError(null);
    setTaskHashes([]);
    setTorrentInfo(new Map());
    setReadyToAnimate(false);
    setLoading(true);
    initialLoadDoneRef.current = false;
    firstPollReceivedRef.current = false;

    const requestId = latestRequestIdRef.current + 1;
    latestRequestIdRef.current = requestId;

    // Reveal content only after BOTH hashes and first poll are done.
    const tryReveal = () => {
      if (initialLoadDoneRef.current && firstPollReceivedRef.current && requestId === latestRequestIdRef.current) {
        setLoading(false);
        requestAnimationFrame(() => {
          requestAnimationFrame(() => setReadyToAnimate(true));
        });
      }
    };

    void loadHashes(requestId).finally(() => {
      if (requestId === latestRequestIdRef.current) {
        initialLoadDoneRef.current = true;
        tryReveal();
      }
    });

    // Polling: first result unblocks reveal, subsequent updates are silent
    stopPollingRef.current?.();
    stopPollingRef.current = mikanApi.startProgressPolling(POLLING_INTERVAL_MS, (progresses) => {
      if (requestId !== latestRequestIdRef.current) return;
      setTorrentInfo((prev) => {
        if (prev.size === 0 && progresses.size === 0) return prev;
        return progresses;
      });
      if (!firstPollReceivedRef.current) {
        firstPollReceivedRef.current = true;
        tryReveal();
      }
    });

    return () => {
      latestRequestIdRef.current += 1;
      stopPollingRef.current?.();
      stopPollingRef.current = null;
    };
  }, [open, subscription, manualBangumiId, isManualMode, language, loadHashes]);

  useEffect(() => {
    if (!open) return;
    const handleEsc = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    document.addEventListener("keydown", handleEsc);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "unset";
    };
  }, [open, onClose]);

  const tasks = useMemo<VisualTask[]>(() => {
    return taskHashes.map((item, index) => {
      const hash = normalizeHash(item.hash);
      const info = hash ? torrentInfo.get(hash) : undefined;
      const resolved = resolveTaskFromQb(item, info);
      return {
        key: hash || `task-${index}`,
        hash,
        index,
        title: item.title,
        status: resolved.status,
        progress: resolved.progress,
        speed: resolved.speed,
        publishedAt: item.publishedAt,
        rawStatus: resolved.rawStatus,
        errorMessage: resolved.errorMessage,
        fileSize: item.fileSize,
      };
    });
  }, [taskHashes, torrentInfo]);

  // Group tasks by status, each group sorted by EP descending (latest first)
  const groupedTasks = useMemo(() => {
    const groups: Record<TaskVisualStatus, VisualTask[]> = {
      downloading: [],
      queued: [],
      error: [],
      success: [],
    };
    for (const task of tasks) {
      groups[task.status].push(task);
    }
    // Sort each group by index descending (latest EP first)
    for (const key of SECTION_ORDER) {
      groups[key].sort((a, b) => b.index - a.index);
    }
    return groups;
  }, [tasks]);

  const statusCounts = useMemo(() => {
    const c = { success: 0, downloading: 0, queued: 0, error: 0 };
    for (const task of tasks) c[task.status] += 1;
    return c;
  }, [tasks]);

  const totalDownloadSpeed = useMemo(() => {
    return tasks.reduce((sum, t) => (t.status === "downloading" ? sum + t.speed : sum), 0);
  }, [tasks]);

  const overallProgress = useMemo(() => {
    if (tasks.length === 0) return 0;
    let weightedSum = 0;
    let totalWeight = 0;
    for (const task of tasks) {
      const w = typeof task.fileSize === "number" && task.fileSize > 0 ? task.fileSize : 1;
      weightedSum += task.progress * w;
      totalWeight += w;
    }
    if (totalWeight <= 0) return 0;
    return Math.round((weightedSum / totalWeight) * 100) / 100;
  }, [tasks]);

  const latestPublishedAt = useMemo(() => {
    if (taskHashes.length === 0) return null;
    return formatDateTime(taskHashes[0]?.publishedAt);
  }, [taskHashes]);

  if (!open) return null;

  const hasActiveDownloads = statusCounts.downloading > 0;
  const backdropUrl = anime.images?.landscape || anime.images?.portrait || "";

  const modalContent = (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" onClick={onClose}>
      <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" />

      <div
        className="relative z-10 w-full max-w-2xl overflow-hidden rounded-2xl bg-white shadow-2xl"
        onClick={(event) => event.stopPropagation()}
      >
        {/* ── Banner (pure backdrop, no text) ── */}
        <div className="relative h-28 overflow-hidden bg-gray-900">
          {backdropUrl && (
            <img src={backdropUrl} alt="" className="absolute inset-0 h-full w-full object-cover opacity-50 blur-[1px]" />
          )}
          <div className="absolute inset-0 bg-gradient-to-t from-black/50 to-transparent" />

          {/* close – use div to avoid global button styles (background, border, padding) */}
          <div
            role="button"
            tabIndex={0}
            onClick={onClose}
            onKeyDown={(e) => { if (e.key === "Enter") onClose(); }}
            className="absolute right-3 top-3 z-20 flex h-8 w-8 cursor-pointer items-center justify-center text-white transition-transform duration-150 hover:scale-110"
            aria-label="Close"
          >
            <CloseIcon className="w-5 h-5" />
          </div>
        </div>

        {/* ── Title + progress summary (white area) ── */}
        <div className="border-b border-gray-100 px-5 pt-4 pb-3">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-bold text-gray-900 leading-tight truncate">{primaryTitle}</h2>
            {subscription && onUnsubscribed && (
              <button
                type="button"
                onClick={() => setShowUnsubConfirm(true)}
                className="group group/subscribe"
                style={{ all: "unset", padding: "0", cursor: "pointer" }}
                aria-label={language === "zh" ? "取消订阅" : "Unsubscribe"}
                title={language === "zh" ? "取消订阅" : "Unsubscribe"}
              >
                <span
                  className={`inline-flex h-8 items-center justify-center gap-0 overflow-hidden rounded-md bg-transparent text-gray-600 transition-all duration-200 w-8 ${language === "zh" ? "group-hover/subscribe:w-20" : "group-hover/subscribe:w-24"} group-hover/subscribe:gap-1`}
                >
                  <CheckIcon className="h-5 w-5 shrink-0 text-green-600" />
                  <span
                    className={`max-w-0 overflow-hidden whitespace-nowrap text-xs font-bold opacity-0 transition-all duration-200 ${language === "zh" ? "group-hover/subscribe:max-w-12" : "group-hover/subscribe:max-w-16"} group-hover/subscribe:opacity-100`}
                  >
                    {language === "zh" ? "取消订阅" : "Unsubscribe"}
                  </span>
                </span>
              </button>
            )}
          </div>
          {secondaryTitle && <p className="mt-0.5 text-sm text-gray-400 truncate">{secondaryTitle}</p>}
          <div className="mt-1 flex items-center gap-2 text-xs text-gray-400">
            <span>{texts.title}</span>
            <span>·</span>
            <span className="tabular-nums">{tasks.length} {texts.episodes}</span>
            {latestPublishedAt && (
              <>
                <span>·</span>
                <span>{texts.latestUpdate} {latestPublishedAt}</span>
              </>
            )}
          </div>

          {/* progress bar */}
          <div className="mt-3 flex items-center gap-3">
            <div className="flex items-baseline gap-1">
              <span className="text-2xl font-bold tabular-nums text-gray-900">{Math.round(overallProgress)}</span>
              <span className="text-sm text-gray-400">%</span>
            </div>

            <div className="flex-1">
              <div className="h-2 overflow-hidden rounded-full bg-gray-100">
                <div
                  className={`h-full rounded-full ${readyToAnimate ? "transition-[width] duration-500" : ""} ${overallProgress >= 99.9 ? "bg-emerald-500" : "bg-blue-500"}`}
                  style={{ width: `${overallProgress}%` }}
                />
              </div>
            </div>

            {hasActiveDownloads && (
              <div className="flex-shrink-0 flex items-center gap-1 rounded-full bg-blue-50 px-2.5 py-1">
                <DownloadIcon className="h-3.5 w-3.5 text-blue-500" />
                <span className="text-xs font-medium text-blue-600 tabular-nums">{formatSpeed(totalDownloadSpeed, language)}</span>
              </div>
            )}
          </div>

          <div className="mt-1.5 text-xs text-gray-400">
            <span className="tabular-nums font-medium text-gray-600">{statusCounts.success}</span>
            <span> / {tasks.length} {texts.completed}</span>
          </div>
        </div>

        {/* ── Grouped task sections ── */}
        <div className="max-h-[calc(90vh-16rem)] overflow-y-auto">
          {loading && (
            <div className="flex items-center justify-center py-12">
              <LoadingSpinner />
            </div>
          )}

          {error && (
            <div className="px-5 py-8">
              <ErrorMessage message={error} />
            </div>
          )}

          {!loading && !error && tasks.length === 0 && (
            <div className="flex items-center justify-center py-12">
              <p className="text-sm text-gray-400">{texts.noTask}</p>
            </div>
          )}

          {!loading && !error && tasks.length > 0 && (
            <div className="divide-y divide-gray-100 py-1">
              {SECTION_ORDER.map((status) => (
                <TaskSection
                  key={status}
                  status={status}
                  tasks={groupedTasks[status]}
                  language={language}
                  defaultExpanded
                  animate={readyToAnimate}
                />
              ))}
            </div>
          )}
        </div>

        {/* ── Unsubscribe confirm overlay ── */}
        {showUnsubConfirm && (
          <div className="absolute inset-0 z-30 flex items-center justify-center bg-black/40 rounded-lg">
            <div className="mx-4 w-full max-w-sm rounded-lg bg-white p-5 shadow-xl" onClick={(e) => e.stopPropagation()}>
              <h3 className="text-base font-semibold text-gray-900">
                {language === "zh" ? "确认取消订阅" : "Confirm unsubscribe"}
              </h3>
              <p className="mt-2 text-sm text-gray-600">
                {language === "zh"
                  ? "确认取消订阅吗？您希望如何处理已下载的动漫文件？"
                  : "Confirm unsubscribe? How would you like to handle downloaded anime files?"}
              </p>
              <div className="mt-4 flex flex-wrap items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setShowUnsubConfirm(false)}
                  disabled={unsubBusy}
                  style={{ all: "unset", cursor: unsubBusy ? "not-allowed" : "pointer" }}
                  className="rounded-md border border-gray-300 bg-gray-100 px-3 py-1.5 text-sm text-gray-900 transition-colors hover:bg-gray-200 disabled:opacity-60"
                >
                  {language === "zh" ? "回退" : "Back"}
                </button>
                <button
                  type="button"
                  onClick={() => void handleUnsubscribeAction("keep_files")}
                  disabled={unsubBusy}
                  style={{ all: "unset", cursor: unsubBusy ? "not-allowed" : "pointer" }}
                  className="rounded-md border border-gray-300 bg-gray-100 px-3 py-1.5 text-sm text-gray-900 transition-colors hover:bg-gray-200 disabled:opacity-60"
                >
                  {unsubBusy
                    ? (language === "zh" ? "处理中..." : "Processing...")
                    : (language === "zh" ? "取订但保留文件" : "Unsubscribe + Keep")}
                </button>
                <button
                  type="button"
                  onClick={() => void handleUnsubscribeAction("delete_files")}
                  disabled={unsubBusy}
                  style={{ all: "unset", cursor: unsubBusy ? "not-allowed" : "pointer" }}
                  className="rounded-md border border-red-300 bg-red-50 px-3 py-1.5 text-sm text-red-700 transition-colors hover:bg-red-100 disabled:opacity-60"
                >
                  {unsubBusy
                    ? (language === "zh" ? "处理中..." : "Processing...")
                    : (language === "zh" ? "取订并删除文件" : "Unsubscribe + Delete")}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );

  return createPortal(modalContent, document.body);
}

export default SubscriptionDownloadDetailModal;
