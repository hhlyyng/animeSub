import { useMemo } from "react";
import type { ParsedRssItem, TorrentInfo } from "../../../types/mikan";
import { DownloadActionButton } from "./DownloadActionButton";
import type { DownloadActionState } from "./DownloadActionButton";

type DownloadEpisodeGroupProps = {
  language: "zh" | "en";
  groupKey: string;
  episode: number | null;
  isCollectionGroup?: boolean;
  collapsed: boolean;
  onToggleCollapse: (groupKey: string) => void;
  items: ParsedRssItem[];
  downloadStatus: Map<string, boolean>;
  stateOverrides: Map<string, DownloadActionState>;
  torrentInfo: Map<string, TorrentInfo>;
  busyHash: string | null;
  onDownload: (item: ParsedRssItem) => Promise<void>;
  onPause: (hash: string) => Promise<void>;
  onResume: (hash: string) => Promise<void>;
  onRemove: (hash: string, title: string) => Promise<void>;
};

type ButtonState = DownloadActionState;

function isCompleted(info: TorrentInfo | undefined): boolean {
  if (!info) return false;
  if (isPaused(info)) return false;
  if (info.progress >= 99.9) return true;
  const normalized = info.state.toLowerCase();
  return (
    normalized.includes("completed") ||
    normalized.includes("uploading") ||
    normalized.includes("forcedup") ||
    normalized.includes("checkingup") ||
    normalized.includes("stalledup") ||
    normalized.includes("queuedup")
  );
}

function isDownloading(info: TorrentInfo | undefined): boolean {
  if (!info) return false;
  const normalized = info.state.toLowerCase();
  return (
    normalized.includes("downloading") ||
    normalized.includes("forceddl") ||
    normalized.includes("metadl") ||
    normalized.includes("allocating") ||
    normalized.includes("checkingdl")
  );
}

function isPaused(info: TorrentInfo | undefined): boolean {
  if (!info) return false;
  const normalized = info.state.toLowerCase();
  return (
    normalized.includes("paused") ||
    normalized.includes("stopped") ||
    normalized.includes("stop") ||
    normalized.includes("stalldl") ||
    normalized.includes("queueddl") ||
    normalized.includes("pending")
  );
}

function resolveButtonState(info: TorrentInfo | undefined, hasRecord: boolean): ButtonState {
  if (!hasRecord) {
    return "idle";
  }
  if (isPaused(info)) {
    return "paused";
  }
  if (isCompleted(info)) {
    return "completed";
  }
  if (isDownloading(info)) {
    return "downloading";
  }
  if (info && info.progress > 0) {
    return "paused";
  }
  return "paused";
}

function formatFileSize(fileSize?: number, formattedSize?: string): string | null {
  if (formattedSize && formattedSize.trim().length > 0) {
    return formattedSize.trim();
  }
  if (typeof fileSize !== "number" || fileSize <= 0) {
    return null;
  }

  const kb = 1024;
  const mb = kb * 1024;
  const gb = mb * 1024;
  if (fileSize < kb) return `${fileSize} B`;
  if (fileSize < mb) return `${(fileSize / kb).toFixed(1)} KB`;
  if (fileSize < gb) return `${(fileSize / mb).toFixed(1)} MB`;
  return `${(fileSize / gb).toFixed(2)} GB`;
}

function toResolutionTag(resolution: string | null): string | null {
  if (!resolution) return null;
  const raw = resolution.trim();
  if (!raw) return null;

  const lower = raw.toLowerCase();
  if (lower === "4k") {
    return "4K";
  }

  if (lower.endsWith("p")) {
    return `${lower.slice(0, -1)}P`;
  }

  return raw.toUpperCase();
}

function detectSubtitleDisplay(item: ParsedRssItem): string | null {
  const rawSubtitle = item.subtitleType?.trim() ?? "";
  if (rawSubtitle.length > 0) {
    return rawSubtitle;
  }
  return "\u65e0\u5b57\u5e55";
}

function formatUpdatedAgo(value: string): string | null {
  const publishedAt = new Date(value);
  if (Number.isNaN(publishedAt.getTime())) {
    return null;
  }

  const diffMs = Date.now() - publishedAt.getTime();
  if (diffMs <= 0) {
    return "\u66f4\u65b0\u4e8e \u521a\u521a";
  }

  const minutes = Math.floor(diffMs / (60 * 1000));
  if (minutes < 1) {
    return "\u66f4\u65b0\u4e8e \u521a\u521a";
  }

  if (minutes < 60) {
    return `\u66f4\u65b0\u4e8e ${minutes} \u5206\u949f\u524d`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `\u66f4\u65b0\u4e8e ${hours} \u5c0f\u65f6\u524d`;
  }

  const days = Math.floor(hours / 24);
  if (days < 30) {
    return `\u66f4\u65b0\u4e8e ${days} \u5929\u524d`;
  }

  return `\u66f4\u65b0\u4e8e ${publishedAt.toLocaleDateString()}`;
}

function toDisplayTags(item: ParsedRssItem): { tags: string[]; weakTag: string | null } {
  const tags: string[] = [];

  const resolutionTag = toResolutionTag(item.resolution);
  if (resolutionTag) {
    tags.push(resolutionTag);
  }

  const subtitleTag = detectSubtitleDisplay(item);
  if (subtitleTag) {
    tags.push(subtitleTag);
  }

  const sizeTag = formatFileSize(item.fileSize, item.formattedSize);
  if (sizeTag) {
    tags.push(sizeTag);
  }

  return {
    tags,
    weakTag: formatUpdatedAgo(item.publishedAt),
  };
}

export function DownloadEpisodeGroup({
  language,
  groupKey,
  episode,
  isCollectionGroup = false,
  collapsed,
  onToggleCollapse,
  items,
  downloadStatus,
  stateOverrides,
  torrentInfo,
  busyHash,
  onDownload,
  onPause,
  onResume,
  onRemove,
}: DownloadEpisodeGroupProps) {
  const title = isCollectionGroup ? "\u5408\u96c6" : episode !== null ? `EP ${episode}` : "Unknown Episode";

  const rows = useMemo(
    () =>
      items.map((item) => {
        const hash = item.torrentHash?.trim().toUpperCase() ?? "";
        const hasHash = hash.length > 0;
        const canDownload = item.canDownload ?? hasHash;
        const info = hasHash ? torrentInfo.get(hash) : undefined;
        const hasRecord = hasHash ? (downloadStatus.get(hash) ?? false) : false;
        const state = (hasHash ? stateOverrides.get(hash) : undefined) ?? resolveButtonState(info, hasRecord);
        const progress = state === "completed" ? 100 : info?.progress ?? 0;
        const display = toDisplayTags(item);
        return {
          item,
          hash,
          canDownload,
          state,
          progress,
          tags: display.tags,
          weakTag: display.weakTag,
        };
      }),
    [items, torrentInfo, downloadStatus, stateOverrides]
  );

  return (
    <section className="rounded-xl border border-gray-200 bg-white shadow-sm">
      <header>
        <button
          type="button"
          onClick={() => onToggleCollapse(groupKey)}
          className="flex w-full items-center justify-between border-b border-gray-100 bg-gray-50 px-4 py-3 text-left transition-colors hover:bg-gray-100"
        >
          <h4 className="text-sm font-semibold text-gray-900">{title}</h4>
          <div className="flex items-center gap-3">
            <span className="text-xs text-gray-500">{rows.length} sources</span>
            <span
              className={`inline-flex h-[1.6rem] w-[1.6rem] items-center justify-center text-gray-500 transition-transform duration-200 ${
                collapsed ? "rotate-0" : "rotate-180"
              }`}
              aria-hidden="true"
            >
              <svg viewBox="0 0 24 24" fill="none" className="h-full w-full" stroke="currentColor" strokeWidth={2.2}>
                <path d="M6 9l6 6 6-6" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            </span>
          </div>
        </button>
      </header>

      {!collapsed && (
        <div className="divide-y divide-gray-100">
          {rows.map(({ item, hash, canDownload, state, progress, tags, weakTag }) => {
            const subgroup = item.subgroup?.trim() || (language === "zh" ? "\u672a\u77e5\u5b57\u5e55\u7ec4" : "Unknown Group");
            const hasHash = hash.length > 0;
            const isBusy = hasHash ? busyHash === hash : false;

            const handleAction = async () => {
              if (state === "idle") {
                if (!canDownload) {
                  return;
                }
                await onDownload(item);
                return;
              }
              if (!hasHash) {
                return;
              }
              if (state === "downloading") {
                await onPause(hash);
                return;
              }
              if (state === "paused") {
                await onResume(hash);
                return;
              }
              await onRemove(hash, item.title);
            };

            const rowKey = hasHash ? hash : `${item.torrentUrl}-${item.magnetLink}-${item.publishedAt}-${item.title}`;

            return (
              <article key={rowKey} className="flex items-center gap-4 px-4 py-3">
                <div className="w-28 shrink-0">
                  <span className="inline-flex items-center rounded-md border border-blue-100 bg-blue-50 px-2.5 py-1 text-xs font-semibold text-blue-800">
                    {subgroup}
                  </span>
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap gap-1.5">
                    {tags.map((tag) => (
                      <span
                        key={`${rowKey}-${tag}`}
                        className="inline-flex items-center rounded-full border border-gray-200 bg-gray-50 px-2 py-0.5 text-[11px] text-gray-600"
                      >
                        {tag}
                      </span>
                    ))}
                    {weakTag && (
                      <span className="inline-flex items-center rounded-full border border-gray-100 bg-gray-50/60 px-2 py-0.5 text-[11px] text-gray-400">
                        {weakTag}
                      </span>
                    )}
                  </div>
                </div>

                <div className="shrink-0">
                  <DownloadActionButton
                    state={state}
                    progress={progress}
                    disabled={isBusy || (state === "idle" && !canDownload) || (!hasHash && state !== "idle")}
                    secondaryAction={
                      state === "paused" && hasHash
                        ? {
                            onClick: () => {
                              void onRemove(hash, item.title);
                            },
                            disabled: isBusy,
                            ariaLabel: "Remove torrent task",
                          }
                        : undefined
                    }
                    onClick={handleAction}
                  />
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}

export default DownloadEpisodeGroup;
