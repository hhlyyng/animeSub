/**
 * Shared torrent state utilities used by both AnimeDetailModal and SubscriptionDownloadDetailModal.
 */

export function normalizeHash(hash: string | null | undefined): string {
  return hash?.trim().toUpperCase() ?? "";
}

export function clampPercent(value: number): number {
  if (!Number.isFinite(value)) return 0;
  if (value <= 1) return Math.round(Math.max(0, Math.min(100, value * 100)) * 100) / 100;
  return Math.round(Math.max(0, Math.min(100, value)) * 100) / 100;
}

export function normalizeTorrentState(state?: string): string {
  return (state ?? "").toLowerCase();
}

export function isTorrentCompletedState(state?: string, progress?: number): boolean {
  if (typeof progress === "number" && clampPercent(progress) >= 99.9) return true;
  const n = normalizeTorrentState(state);
  return (
    n.includes("completed") ||
    n.includes("uploading") ||
    n.includes("forcedup") ||
    n.includes("checkingup") ||
    n.includes("stalledup") ||
    n.includes("queuedup")
  );
}

export function isTorrentDownloadingState(state?: string): boolean {
  const n = normalizeTorrentState(state);
  return (
    n.includes("downloading") ||
    n.includes("forceddl") ||
    n.includes("metadl") ||
    n.includes("allocating") ||
    n.includes("checkingdl")
  );
}

export function isTorrentPausedState(state?: string): boolean {
  const n = normalizeTorrentState(state);
  return (
    n.includes("paused") ||
    n.includes("stopped") ||
    n.includes("stop") ||
    n.includes("stalldl") ||
    n.includes("queueddl") ||
    n.includes("pending")
  );
}

/** Alias for isTorrentPausedState — semantically "queued" in the subscription context. */
export const isTorrentQueuedState = isTorrentPausedState;

export function isTorrentErrorState(state?: string): boolean {
  const n = normalizeTorrentState(state);
  return n.includes("error") || n.includes("missingfiles");
}
