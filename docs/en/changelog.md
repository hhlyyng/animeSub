# Changelog

## 2026-02-05

### Fix: Bangumi Token Made Optional

API testing confirmed that Bangumi public endpoints (daily schedule, score ranking, subject details) do not require authentication.

**Changes**:
- Bangumi Token is now optional; missing token uses the public API instead of returning 401/400
- API endpoint changed from `POST /v0/search/subjects` to `GET /v0/subjects?type=2&sort=rank`

### Added: Database Schema

The system uses SQLite for persistent storage. Main tables:

| Table | Description |
|-------|-------------|
| `AnimeInfo` | Aggregated anime data (titles, ratings, images, links) |
| `AnimeImages` | Image cache (posters, backdrops) |
| `Subscriptions` | Subscription configuration |
| `DownloadHistory` | Download history (deduplicated by TorrentHash) |
| `DailyScheduleCache` | Daily schedule cache |

---

## 2026-02-04

### Added: Top 10 Rankings

Three new API endpoints displaying Top 10 anime from different sources:

| Endpoint | Source |
|----------|--------|
| `GET /api/anime/top/bangumi` | Bangumi score ranking |
| `GET /api/anime/top/anilist` | AniList trending |
| `GET /api/anime/top/mal` | MyAnimeList via Jikan API |

Frontend home page gains 3 horizontally-scrollable AnimeFlow card rows.

### Added: Pre-fetch Architecture

Rewrote the backend from real-time API aggregation to a pre-fetch + incremental update model:

- Response time improved from 3–10 seconds to **<100ms**
- Background `AnimePreFetchService` batch-fetches the full week's data every night at 3 AM
- New admin API: `GET /api/admin/prefetch/status`, `POST /api/admin/prefetch`

### Added: TMDB Smart Matching

- **Animation priority**: Prefers Animation genre (genre_id=16) over live-action results
- **Japanese origin priority**: Prefers origin_country=JP when multiple matches exist
- **Multi-season matching**: Uses Bangumi `air_date` year to filter results and match the correct season's artwork
- **Chinese description fallback**: Uses TMDB Chinese summary when Bangumi description is empty

### Added: Mikan RSS Subscription System

Full subscription and auto-download feature:

- Subscription management CRUD (9 REST API endpoints)
- Background `RssPollingService` (polls every 30 minutes)
- Keyword include/exclude filtering, fansub group filtering
- TorrentHash deduplication to prevent re-downloads
- qBittorrent WebUI integration

---

## 2026-02-03

### Fix: Backend Runtime Bugs

Fixed multiple runtime issues after refactoring:

| Issue | Fix |
|-------|-----|
| DI lifecycle conflict (Singleton consuming Scoped) | Changed `AnimeCacheService` to Scoped |
| Exception when modifying headers after response started | Added `HasStarted` check |
| HttpClient BaseAddress path being overridden | Ensured BaseAddress ends with `/`; relative URLs don't start with `/` |
| SQLite foreign key constraint failure | Removed AnimeImages → AnimeInfo FK; tables are now independent |

---

## 2026-02-02

### Improved: AnimeDetailModal

- Modal only blurs the main content area; Sidebar stays sharp (via Zustand shared state)
- Rendered with `createPortal` to `document.body`, escaping parent element style constraints
- Layout restructured to vertical sections (info area + download sources area)
- Displays titles and descriptions in the active language with fallback chains

### Fix: Sidebar Title Alignment

Added `items-center` to `.sidebar-header` to fix vertical misalignment between the toggle button icon and "Anime-Sub" text.

### Refactor: Frontend Spacing Styles

- Merged duplicate `.sidebar-header` definitions in `index.css`
- Added `.content-header` CSS class to centrally manage main content top padding
- Sidebar title and main content title now aligned (both use `pt-16` = 64px)
