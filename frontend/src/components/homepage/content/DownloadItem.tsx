import type { ParsedRssItem, TorrentInfo } from "../../../types/mikan";

type DownloadItemProps = {
  item: ParsedRssItem;
  torrentInfo?: TorrentInfo;
  isDownloaded: boolean;
  onDownload: (item: ParsedRssItem) => Promise<void>;
  onCopyMagnet: (magnetLink: string) => Promise<void>;
  isDownloading: boolean;
};

export function DownloadItem({
  item,
  torrentInfo,
  isDownloaded,
  onDownload,
  onCopyMagnet,
  isDownloading,
}: DownloadItemProps) {
  return (
    <div className="p-3 bg-white rounded-lg border border-gray-200 hover:border-gray-300 transition-colors">
      <div className="flex items-center gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            {item.episode !== null && (
              <span className="inline-flex items-center px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-800 rounded">
                EP {item.episode}
              </span>
            )}
            <span className="text-sm font-medium text-gray-900 truncate">{item.title}</span>
          </div>

          <div className="flex items-center gap-2 text-xs text-gray-500">
            {item.resolution && (
              <span className="inline-flex items-center px-2 py-0.5 bg-gray-100 text-gray-600 rounded">
                {item.resolution}
              </span>
            )}
            {item.subgroup && (
              <span className="inline-flex items-center px-2 py-0.5 bg-purple-100 text-purple-600 rounded">
                {item.subgroup}
              </span>
            )}
            {item.subtitleType && <span className="text-gray-500">{item.subtitleType}</span>}
          </div>
        </div>

        <div className="flex items-center gap-2 flex-shrink-0">
          {isDownloaded && (
            <span className="inline-flex items-center px-2 py-1 text-xs font-medium bg-green-100 text-green-800 rounded">
              Downloaded
            </span>
          )}

          <button
            onClick={() => onCopyMagnet(item.magnetLink)}
            className="inline-flex items-center px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
            disabled={isDownloading}
          >
            Copy magnet
          </button>

          <button
            onClick={() => onDownload(item)}
            disabled={isDownloaded || isDownloading}
            className="inline-flex items-center px-3 py-1.5 text-sm font-medium rounded focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
          >
            {isDownloading ? "Downloading..." : "Download"}
          </button>
        </div>
      </div>

      {torrentInfo && (torrentInfo.progress > 0 || torrentInfo.downloadSpeed || torrentInfo.eta !== undefined) && (
        <div className="mt-2 pt-2 border-t border-gray-200">
          <div className="text-xs text-gray-500 space-y-1">
            {torrentInfo.progress > 0 && (
              <div className="flex items-center gap-2">
                <span className="font-medium text-gray-700">
                  Progress: {torrentInfo.progress.toFixed(1)}%
                </span>
                <div className="flex-1 bg-gray-200 rounded-full h-2">
                  <div
                    className="bg-blue-600 h-2 rounded-full transition-all duration-300"
                    style={{ width: `${torrentInfo.progress}%` }}
                  />
                </div>
              </div>
            )}

            {torrentInfo.downloadSpeed !== undefined && torrentInfo.downloadSpeed > 0 && (
              <div className="flex items-center justify-between">
                <span className="font-medium text-gray-700">Speed: {formatSpeed(torrentInfo.downloadSpeed)}</span>
              </div>
            )}

            {torrentInfo.eta !== undefined && torrentInfo.eta > 0 && (
              <div className="flex items-center justify-between">
                <span className="font-medium text-gray-700">ETA: {formatTime(torrentInfo.eta)}</span>
              </div>
            )}

            {(torrentInfo.numSeeds !== undefined || torrentInfo.numLeechers !== undefined) && (
              <div className="flex items-center justify-between">
                <span className="font-medium text-gray-700">
                  {torrentInfo.numSeeds !== undefined && `Seeds: ${torrentInfo.numSeeds}`}
                  {torrentInfo.numSeeds !== undefined && torrentInfo.numLeechers !== undefined && " | "}
                  {torrentInfo.numLeechers !== undefined && `Leechers: ${torrentInfo.numLeechers}`}
                </span>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function formatSpeed(bytesPerSecond: number): string {
  if (bytesPerSecond < 1024) return `${bytesPerSecond.toFixed(0)} B/s`;
  if (bytesPerSecond < 1024 * 1024) return `${(bytesPerSecond / 1024).toFixed(1)} KB/s`;
  if (bytesPerSecond < 1024 * 1024 * 1024) return `${(bytesPerSecond / (1024 * 1024)).toFixed(1)} MB/s`;
  return `${(bytesPerSecond / (1024 * 1024 * 1024)).toFixed(1)} GB/s`;
}

function formatTime(seconds: number): string {
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;
  if (minutes < 60) return `${minutes}m ${remainingSeconds}s`;
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return `${hours}h ${remainingMinutes}m`;
}
