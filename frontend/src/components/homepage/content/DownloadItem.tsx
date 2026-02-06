import type { ParsedRssItem } from '../../../types/mikan';

type DownloadItemProps = {
  item: ParsedRssItem;
  isDownloaded: boolean;
  onDownload: (item: ParsedRssItem) => Promise<void>;
  onCopyMagnet: (magnetLink: string) => Promise<void>;
  isDownloading: boolean;
};

export function DownloadItem({ item, isDownloaded, onDownload, onCopyMagnet, isDownloading }: DownloadItemProps) {
  return (
    <div className="flex items-center gap-3 p-3 bg-white rounded-lg border border-gray-200 hover:border-gray-300 transition-colors">
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 mb-1">
          {item.episode !== null && (
            <span className="inline-flex items-center px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-800 rounded">
              EP {item.episode}
            </span>
          )}
          <span className="text-sm font-medium text-gray-900 truncate">
            {item.title}
          </span>
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
          {item.subtitleType && (
            <span className="text-gray-500">
              {item.subtitleType}
            </span>
          )}
        </div>
      </div>

      <div className="flex items-center gap-2 flex-shrink-0">
        {isDownloaded && (
          <span className="inline-flex items-center px-2 py-1 text-xs font-medium bg-green-100 text-green-800 rounded">
            已下载
          </span>
        )}
        <button
          onClick={() => onCopyMagnet(item.magnetLink)}
          className="inline-flex items-center px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
          disabled={isDownloading}
        >
          复制磁力
        </button>
        <button
          onClick={() => onDownload(item)}
          disabled={isDownloaded || isDownloading}
          className={`inline-flex items-center px-3 py-1.5 text-sm font-medium rounded focus:outline-none focus:ring-2 focus:ring-offset-2 ${
            isDownloaded
              ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
              : 'bg-blue-600 text-white hover:bg-blue-700 focus:ring-blue-500'
          }`}
        >
          {isDownloading ? '下载中...' : '下载'}
        </button>
      </div>
    </div>
  );
}
