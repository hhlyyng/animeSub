# Features

## Daily Schedule

The home page displays today's airing anime. Data is sourced from Bangumi and enriched with English metadata and landscape backdrops from TMDB and AniList.

- Browse the weekly airing schedule with manual date switching
- Each anime card shows: portrait poster, rating, Chinese and English titles
- Click a card to open a detail modal with: landscape backdrop, bilingual description, external links (Bangumi / TMDB / AniList / MAL)
- Data is cached in sessionStorage — no repeated requests on page reload

## Top 10 Rankings

The home page displays three simultaneous Top 10 charts as horizontally-scrollable card rows:

| Chart | Source | Notes |
|-------|--------|-------|
| Bangumi Top 10 | Bangumi score ranking | Authoritative Japanese anime ratings database |
| AniList Trending | AniList trending chart | Based on recent views and discussion activity |
| MAL Top 10 | MyAnimeList via Jikan API | World's largest anime community rankings |

## Search & Resources

Find torrent resources for any anime:

1. Mikan is the primary search source — fetches and parses the anime's RSS page
2. Results list with filters:
   - Resolution (1080p / 720p / 4K)
   - Subtitle type (Simplified Chinese / Traditional Chinese / Multilingual)
   - Fansub group
3. One-click send to qBittorrent

## Subscription Management

Subscriptions are the core of the auto-download feature. Each subscription binds to one anime series; the system polls Mikan RSS periodically and downloads new episodes automatically.

### Creating a Subscription

Click "Subscribe" on any anime detail page or search result. Configure:

| Option | Description |
|--------|-------------|
| Title | Display name |
| Mikan Anime ID | Used to build the RSS URL (from the Mikan anime page) |
| Fansub Group | Restrict to a specific group (leave empty for all) |
| Include Keywords | Comma-separated; resource title must contain these |
| Exclude Keywords | Comma-separated; resources with these words are skipped |

**Example**: Subscribe to 1080p Simplified Chinese releases from ANi

```
Fansub Group: ANi
Include Keywords: 1080p,简体
Exclude Keywords: HEVC
```

### Subscription Status

| Status | Description |
|--------|-------------|
| Enabled | Background polling will process this subscription |
| Disabled | Polling paused; download history is retained |

### Manual Check

Click the "Check" button on any subscription to immediately trigger an RSS check without waiting for the next scheduled poll.

## Auto Download

The background `RssPollingService` runs at the configured interval (default: 30 minutes) and processes all enabled subscriptions.

### Polling Flow

```
1. Check if EnablePolling is true
2. Fetch all enabled subscriptions
3. For each subscription:
   a. Request RSS feed from Mikan
   b. Parse XML, extract torrent list
   c. Query DownloadHistory by TorrentHash (deduplication)
   d. For each new resource:
      - Match fansub group filter
      - Apply keyword include/exclude filters
   e. Push filtered torrents to qBittorrent
   f. Record in DownloadHistory (prevents re-download)
   g. Update subscription LastDownloadAt and DownloadCount
```

### Download History

Each download records:

- Resource title
- Torrent hash (unique identifier for deduplication)
- File size
- Download status (Pending / Downloading / Completed / Failed / Skipped)
- Discovery time and download time

## Download Manager

View the status of all torrents in qBittorrent:

- Name, size, and progress
- Download/upload speed
- Pause / Resume / Delete actions (synced to qBittorrent)

## Web Settings

All configuration can be done in the browser — no SSH or config file editing needed:

- **qBittorrent Connection**: Host/port/credentials with connection test
- **TMDB Token**: Set or update the API token
- **Polling Interval**: Change the RSS check frequency
- **Language**: Switch between Chinese and English (persisted to localStorage)
