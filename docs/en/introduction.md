# Introduction

AnimeSub is a self-hosted anime subscription and auto-download application. It aggregates multiple anime databases to display daily airing schedules, and automatically tracks and downloads new episodes via Mikan RSS feeds.

## Features

- **Daily Schedule**: Weekly airing timetable sourced from Bangumi, with TMDB landscape backdrops
- **Top 10 Rankings**: Three simultaneous charts from Bangumi, AniList Trending, and MyAnimeList
- **Search & Download**: Search for anime resources via Mikan (primary source), filterable by resolution and subtitle type
- **Subscription Management**: Create/edit/delete subscriptions with keyword include/exclude and fansub group filtering
- **Auto Download**: Background RSS polling service pushes new torrents to qBittorrent automatically
- **Download Manager**: View torrent list, pause/resume/delete tasks, real-time progress tracking
- **Web Settings**: Configure everything in the browser — no config file editing required

## System Architecture

### Data Aggregation Flow

```
Frontend Request → AnimeController
    ├── Bangumi API   → Schedule, ratings, portrait posters, air_date (primary)
    ├── TMDB API      → English metadata, landscape backdrops (smart matching)
    └── AniList GraphQL → English titles/descriptions (fallback)
    ↓
AnimeInfo[] → Frontend caches in sessionStorage
```

**TMDB Smart Matching**:
- Prioritizes Animation genre (genre_id=16) over live-action results
- Prefers Japanese origin (origin_country=JP) when multiple matches exist
- Uses Bangumi `air_date` year to filter multi-season series and match the correct season's artwork

### Subscription & Download Flow

```
User Creates Subscription → SubscriptionController
    ↓
RssPollingService (Background, every 30 min)
    ├── Fetch Mikan RSS feed
    ├── Filter by fansub group / keywords
    ├── Deduplicate against DownloadHistory (by torrent hash)
    └── Push new torrents to qBittorrent
```

### Pre-fetch Architecture

To achieve sub-100ms response times, the system pre-fetches the full week's airing data nightly and stores it in SQLite:

```
Pre-fetch Service (runs at 3 AM daily)
    ↓
Fetch full calendar → Batch-aggregate Bangumi + TMDB + AniList data
    ↓
Write to SQLite → Frontend reads from DB (<100ms)
```

| Metric | Real-time Aggregation (old) | Pre-fetch Architecture (current) |
|--------|-----------------------------|----------------------------------|
| Response time | 3–10 seconds | <100ms |
| API call timing | On every request | Nightly batch only |
| Failure impact | External API outage affects users | Database as fallback |

## Tech Stack

| Layer | Technology |
|-------|------------|
| Frontend | React 19 + TypeScript + Vite + Tailwind CSS + Zustand |
| Backend | .NET 9 ASP.NET Core |
| Database | SQLite (via Entity Framework Core) |
| Containerization | Docker + Docker Compose |
| Authentication | JWT (HMAC-SHA256) + bcrypt |

## External Service Dependencies

| Service | Purpose | Required? |
|---------|---------|-----------|
| [Bangumi](https://bangumi.tv) | Daily schedule, ratings, cover images | Yes |
| [TMDB](https://www.themoviedb.org) | English metadata, landscape backdrops | No (disables English content and backdrops) |
| [AniList](https://anilist.co) | English title/description fallback | No |
| [Mikan](https://mikanani.me) | Anime RSS torrent feeds | Required for subscriptions |
| qBittorrent | Torrent download management | Required for downloads |
