import { useMemo } from "react";
import type { ParsedRssItem, TorrentInfo } from "../../../types/mikan";
import { DownloadActionButton } from "./DownloadActionButton";

type DownloadEpisodeGroupProps = {
  episode: number | null;
  isCollectionGroup?: boolean;
  items: ParsedRssItem[];
  downloadStatus: Map<string, boolean>;
  torrentInfo: Map<string, TorrentInfo>;
  busyHash: string | null;
  onDownload: (item: ParsedRssItem) => Promise<void>;
  onPause: (hash: string) => Promise<void>;
  onResume: (hash: string) => Promise<void>;
  onRemove: (hash: string) => Promise<void>;
};

type ButtonState = "idle" | "downloading" | "paused" | "completed";

function isCompleted(info: TorrentInfo | undefined): boolean {
  if (!info) return false;
  if (info.progress >= 99.9) return true;
  const normalized = info.state.toLowerCase();
  return (
    normalized.includes("completed") ||
    normalized.includes("upload") ||
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
    normalized.includes("stalldl") ||
    normalized.includes("queueddl") ||
    normalized.includes("pending")
  );
}

function resolveButtonState(info: TorrentInfo | undefined, hasRecord: boolean): ButtonState {
  if (!hasRecord) {
    return "idle";
  }
  if (isCompleted(info)) {
    return "completed";
  }
  if (isDownloading(info)) {
    return "downloading";
  }
  if (isPaused(info)) {
    return "paused";
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

function detectSubtitleDisplay(item: ParsedRssItem): string | null {
  const rawSubtitle = item.subtitleType?.trim() ?? "";
  const source = `${rawSubtitle} ${item.title}`;

  if (/(?:\u5185\u5d4c|\u5167\u5d4c|\u5185\u5c01|\u5167\u5c01|\u5185\u6302|\u5167\u639b|internal|softsub)/i.test(source)) {
    return "\u5b57\u5e55 \u5185\u5d4c";
  }

  if (/(?:\u5916\u6302|\u5916\u639b|\u5916\u7f6e|\u5916\u5c01|external|hard.?sub)/i.test(source)) {
    return "\u5b57\u5e55 \u5916\u7f6e";
  }

  if (rawSubtitle.length > 0) {
    return `\u5b57\u5e55 ${rawSubtitle}`;
  }

  return null;
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

  if (item.resolution) {
    tags.push(`\u5206\u8fa8\u7387 ${item.resolution}`);
  }

  const subtitleTag = detectSubtitleDisplay(item);
  if (subtitleTag) {
    tags.push(subtitleTag);
  }

  const sizeTag = formatFileSize(item.fileSize, item.formattedSize);
  if (sizeTag) {
    tags.push(`\u5927\u5c0f ${sizeTag}`);
  }

  return {
    tags,
    weakTag: formatUpdatedAgo(item.publishedAt),
  };
}

export function DownloadEpisodeGroup({
  episode,
  isCollectionGroup = false,
  items,
  downloadStatus,
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
        const state = resolveButtonState(info, hasRecord);
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
    [items, torrentInfo, downloadStatus]
  );

  return (
    <section className="rounded-xl border border-gray-200 bg-white shadow-sm">
      <header className="flex items-center justify-between border-b border-gray-100 bg-gray-50 px-4 py-3">
        <h4 className="text-sm font-semibold text-gray-900">{title}</h4>
        <span className="text-xs text-gray-500">{rows.length} sources</span>
      </header>

      <div className="divide-y divide-gray-100">
        {rows.map(({ item, hash, canDownload, state, progress, tags, weakTag }) => {
          const subgroup = item.subgroup?.trim() || "Unknown Group";
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
            await onRemove(hash);
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
                  onClick={handleAction}
                />
              </div>
            </article>
          );
        })}
      </div>
    </section>
  );
}

export default DownloadEpisodeGroup;
