# Developer Reference

This document records the current system architecture, source file roles, and the complete backend API reference.

---

## System Architecture

### Overall Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         User Browser                            │
│                                                                  │
│  React 19 + TypeScript + Vite + Tailwind CSS + Zustand          │
│                                                                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────┐  │
│  │ Schedule │  │ Search/DL│  │  Subscr. │  │   Settings   │  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └──────┬───────┘  │
│       └─────────────┴──────────────┴────────────────┘          │
│                          authFetch()                            │
│                   (Bearer JWT + 401 redirect)                   │
└─────────────────────────────┬───────────────────────────────────┘
                              │ HTTP / JSON
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     ASP.NET Core 9 Backend                       │
│                      localhost:5072                              │
│                                                                  │
│  Middleware Pipeline:                                           │
│  CorrelationId → ExceptionHandler → PerfMonitor → ReqLogging   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                      Controllers                          │  │
│  │  AnimeController  │ AuthController  │ SubscriptionCtrl   │  │
│  │  MikanController  │ AdminController │ SettingsController  │  │
│  │                   HealthController                         │  │
│  └──────────┬────────────────────────────────────────────────┘  │
│             │                                                    │
│  ┌──────────▼────────────────────────────────────────────────┐  │
│  │                     Services Layer                         │  │
│  │                                                            │  │
│  │  AnimeAggregationService   SubscriptionService            │  │
│  │  AnimePoolService          AuthService                     │  │
│  │  QBittorrentService        MikanClient                     │  │
│  └──────────┬──────────────────────────┬──────────────────────┘  │
│             │                          │                          │
│  ┌──────────▼──────────┐  ┌───────────▼───────────────────────┐  │
│  │   Repositories      │  │      Background Services           │  │
│  │   AnimeRepository   │  │  AnimePreFetchService  (03:00/day) │  │
│  │ SubscriptionRepo    │  │  RssPollingService    (every 30m)  │  │
│  └──────────┬──────────┘  │  DownloadProgressSync (continuous) │  │
│             │              │  AnimeTitleBackfill   (on startup) │  │
│  ┌──────────▼──────────┐  │  MikanFeedCleanup     (periodic)  │  │
│  │   SQLite Database   │  └───────────────────────────────────┘  │
│  │   (anime.db)        │                                          │
│  └─────────────────────┘                                          │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┼──────────────────────┐
          ▼                   ▼                        ▼
┌─────────────────┐  ┌──────────────────┐  ┌─────────────────────┐
│   Bangumi API   │  │    TMDB API      │  │   AniList GraphQL   │
│ api.bgm.tv/v0   │  │ api.tmdb.org/3   │  │  graphql.anilist.co │
│                 │  │                  │  │                     │
│ Daily schedule  │  │ English metadata │  │ English title/desc  │
│ Score rankings  │  │ Landscape images │  │ Trending rankings   │
│ Anime details   │  │ Smart season match│  │ (fallback source)  │
└─────────────────┘  └──────────────────┘  └─────────────────────┘
          │
          ├─────────────────────────┐
          ▼                         ▼
┌─────────────────┐       ┌──────────────────────┐
│   Jikan API     │       │     Mikan RSS         │
│ api.jikan.moe   │       │   mikanani.me         │
│                 │       │                       │
│ MAL Top 10      │       │ Anime torrent feeds   │
│                 │       │ Fansub group info     │
└─────────────────┘       └──────────┬────────────┘
                                     │
                          ┌──────────▼────────────┐
                          │    qBittorrent WebUI  │
                          │    localhost:8080      │
                          │                       │
                          │  Add/pause/resume     │
                          │  Progress tracking    │
                          └───────────────────────┘
```

### Data Aggregation Flow

```
GET /api/anime/today
        │
        ▼
AnimeAggregationService
        │
        ├─① Query SQLite (GetAnimesByWeekdayAsync)
        │         │
        │    Pre-fetched data exists? ──Yes──► Return DB data (<100ms)
        │         │
        │         No
        │         ▼
        ├─② Bangumi API
        │   GET /v0/calendar → weekly schedule
        │   Per anime: id, name, air_date, score, cover image
        │         │
        ├─③ TMDB API (concurrent)
        │   Search title → smart match (Animation-first / JP-first / year filter)
        │   GET /3/search/tv → GET /3/tv/{id}/season/{n}
        │   Extract: English title, English description, landscape backdrop
        │         │
        └─④ AniList GraphQL (fallback)
            query { Media } → English title/desc (skipped if Bangumi has data)
                    │
                    ▼
             AnimeInfo[] → Return to frontend → Cache in sessionStorage
```

### Pre-fetch Architecture

```
AnimePreFetchService (BackgroundService)
        │
        ├── On startup: RunOnStartup=true → execute immediately
        │
        └── Scheduled: daily at ScheduleHour (default 3 AM)
                  │
                  ├─ GetFullCalendarAsync() → fetch full week from Bangumi
                  ├─ Concurrent aggregation (MaxConcurrency=3)
                  │   Per anime → enrich with TMDB + AniList
                  └─ Batch write to SQLite
                     Set IsPreFetched=true
```

### Subscription Download Flow

```
RssPollingService (every 30 minutes)
        │
        ├─ Check EnablePolling config
        │
        └─ Iterate all subscriptions where IsEnabled=true
                  │
                  ▼
          MikanClient.GetFeedAsync(mikanBangumiId, subgroupId)
                  │
                  ▼
          Parse RSS XML → MikanFeedItem[]
                  │
                  ▼
          Filter pipeline:
          ┌─────────────────────────────────────┐
          │ 1. TorrentHash in DownloadHistory?  │
          │    Yes → Skip (already downloaded)  │
          │    No  → Continue                   │
          │ 2. Fansub group ID matches?         │
          │    No  → Skip                       │
          │ 3. All KeywordInclude present?      │
          │    No  → Skip                       │
          │ 4. Any KeywordExclude present?      │
          │    Yes → Skip                       │
          └─────────────────────────────────────┘
                  │
                  ▼ (passed filter)
          QBittorrentService.AddTorrentAsync()
                  │
                  ▼
          Write to DownloadHistory (Status=Downloading)
          Update Subscription.LastDownloadAt
```

---

## Backend File Reference

### Controllers

| File | Route Prefix | Description |
|------|-------------|-------------|
| `Controllers/AnimeController.cs` | `/api/anime` | Anime data aggregation: daily schedule, Top 10, search, random picks, batch lookup |
| `Controllers/AuthController.cs` | `/api/auth` | Authentication: login, setup wizard, change credentials, background image management |
| `Controllers/SubscriptionController.cs` | `/api/subscription` | Subscription CRUD, manual check, download history, task hash queries |
| `Controllers/MikanController.cs` | `/api/mikan` | Mikan RSS search/parse/filter, torrent download, qBittorrent control |
| `Controllers/AdminController.cs` | `/api/admin` | Admin: pre-fetch status, manual trigger, database stats, data purge |
| `Controllers/SettingsController.cs` | `/api/settings` | Settings: token management, user profile, connection tests |
| `Controllers/HealthController.cs` | `/health` | Health checks: heartbeat, external API dependency status |

### Services/Interfaces

| File | Description |
|------|-------------|
| `IAnimeAggregationService.cs` | Data aggregation interface: schedule, Top 10, search |
| `IAnimePoolService.cs` | Random recommendation pool interface |
| `IAniListClient.cs` | AniList GraphQL client interface |
| `IBangumiClient.cs` | Bangumi HTTP client interface: schedule, rankings, details |
| `IJikanClient.cs` | Jikan (MAL) HTTP client interface |
| `IMikanClient.cs` | Mikan RSS client interface: search, parse, fansub groups |
| `IQBittorrentService.cs` | qBittorrent WebUI interface: add/pause/resume/delete/query |
| `ISubscriptionService.cs` | Subscription business logic interface |
| `ITMDBClient.cs` | TMDB API client interface: search, season artwork |
| `IAuthService.cs` | Auth service interface: login, JWT generation, password change |
| `ITorrentTitleParser.cs` | Torrent title parser interface: episode, resolution, fansub extraction |

### Services/Implementations

| File | Description |
|------|-------------|
| `AnimeAggregationService.cs` | Core aggregation: coordinates Bangumi→TMDB→AniList, DB-first reads |
| `AnimePoolService.cs` | Random pool: Memory cache (L1) + SQLite (L2) fallback, warm on startup |
| `AnimePoolBuilderService.cs` | Pool builder: populates recommendation pool from AniList trending |
| `AniListClient.cs` | AniList GraphQL: trending queries, title/description extraction |
| `BangumiClient.cs` | Bangumi API: `/v0/calendar`, `/v0/subjects` rankings |
| `JikanClient.cs` | Jikan API: `/v4/top/anime` MAL rankings |
| `MikanClient.cs` | Mikan: HtmlAgilityPack page parsing + XML RSS parsing |
| `QBittorrentService.cs` | qBittorrent WebUI wrapper: login cookie management, torrent ops |
| `SubscriptionService.cs` | Subscription business logic: CRUD, RSS check, keyword filtering |
| `TMDBClient.cs` | TMDB: TV search + Animation-priority matching + season artwork |

### Services/Background

| File | Trigger | Description |
|------|---------|-------------|
| `AnimePreFetchService.cs` | Startup + 3 AM daily | Batch pre-fetch full week's schedule into SQLite |
| `RssPollingService.cs` | 30s after startup + every 30m | Check all subscriptions' RSS, auto-download new torrents |
| `DownloadProgressSyncService.cs` | Continuous background | Sync qBittorrent download progress to DownloadHistory |
| `AnimeTitleBackfillService.cs` | On startup | Backfill missing Chinese titles (Bangumi fallback) |
| `MikanFeedSubgroupCleanupService.cs` | Periodic | Clean up expired Mikan feed cache data |

### Services/Repositories

| File | Description |
|------|-------------|
| `IAnimeRepository.cs` / `AnimeRepository.cs` | Anime data access: query by weekday, batch write, Top cache management |
| `ISubscriptionRepository.cs` / `SubscriptionRepository.cs` | Subscription data access: CRUD, query by Bangumi ID, download history |

### Services (Utilities & Support)

| File | Description |
|------|-------------|
| `ApiClientBase.cs` | HTTP client base class: unified error handling, logging, BaseAddress management |
| `AnimeCacheService.cs` | Two-level cache: Memory Cache (L1) + SQLite (L2) |
| `AuthService.cs` | JWT signing (HMAC-SHA256), bcrypt password hashing, user verification |
| `HealthCheckService.cs` | External API connectivity checks (Bangumi/TMDB/AniList) |
| `TokenStorageService.cs` | Encrypted token storage via ASP.NET Core Data Protection API |
| `ResilienceService.cs` | Polly retry policies + circuit breaker for external API calls |
| `Utilities/TitleCleaner.cs` | Anime title normalization: remove noise characters, standardize format |
| `Utilities/TitleLanguageResolver.cs` | Title language detection: Chinese/Japanese/English classification |
| `Utils/TorrentHashHelper.cs` | Torrent hash computation utilities |

### Services/Exceptions

| File | Description |
|------|-------------|
| `ApiException.cs` | Generic API exception base class |
| `BangumiApiException.cs` | Bangumi-specific API exception |
| `ExternalApiException.cs` | External API call failure exception |
| `InvalidCredentialsException.cs` | Authentication credentials invalid |
| `QBittorrentUnavailableException.cs` | qBittorrent connection unavailable |
| `ValidationException.cs` | Request validation failure |

### Data Layer

| File | Description |
|------|-------------|
| `Data/AnimeDbContext.cs` | EF Core SQLite context, defines all DbSets |
| `Data/DbSchemaPatcher.cs` | Lightweight schema migration (patch-mode) |
| `Data/Entities/AnimeInfoEntity.cs` | Aggregated anime data (titles/ratings/images/links/weekday/prefetch status) |
| `Data/Entities/AnimeImagesEntity.cs` | Image cache (portrait poster + landscape backdrop) |
| `Data/Entities/DailyScheduleCacheEntity.cs` | Daily schedule cache (date → Bangumi ID array) |
| `Data/Entities/DownloadHistoryEntity.cs` | Download history (hash dedup, status tracking) |
| `Data/Entities/DownloadSource.cs` | Enum: Subscription / Manual |
| `Data/Entities/MikanFeedCacheEntity.cs` | Mikan RSS feed cache |
| `Data/Entities/MikanFeedItemEntity.cs` | Individual feed record (title/URL/hash) |
| `Data/Entities/MikanSubgroupEntity.cs` | Fansub group ID ↔ name mapping cache |
| `Data/Entities/SubscriptionEntity.cs` | Subscription config (anime/fansub/keywords/status) |
| `Data/Entities/TopAnimeCacheEntity.cs` | Top 10 rankings cache (source/data/expiry) |
| `Data/Entities/UserEntity.cs` | User account (username/bcrypt password hash) |

### Middleware

| File | Description |
|------|-------------|
| `Middleware/CorrelationIdMiddleware.cs` | Adds `X-Correlation-ID` header to every request for distributed tracing |
| `Middleware/ExceptionHandlerMiddleware.cs` | Global exception catch, returns standardized JSON error responses |
| `Middleware/PerformanceMonitoringMiddleware.cs` | Measures request duration, adds `X-Response-Time-Ms` response header |
| `Middleware/RequestResponseLoggingMiddleware.cs` | Logs request/response (path/status/duration) |

### Models

| File/Directory | Description |
|----------------|-------------|
| `Models/AnimeResponse.cs` | Anime list response (includes data source flag: DB/API) |
| `Models/PreFetchStatus.cs` | Pre-fetch service status (running/last run time/statistics) |
| `Models/ErrorResponse.cs` | Standard error response format |
| `Models/TMDBAnimeInfo.cs` | TMDB search result mapping model |
| `Models/AniListAnimeInfo.cs` | AniList GraphQL response mapping |
| `Models/Dtos/AnimeInfoDto.cs` | Aggregated anime DTO for frontend display |
| `Models/Dtos/AnimeImagesDto.cs` | Images DTO |
| `Models/Dtos/ApiResponseDto.cs` | Unified API response wrapper `{ success, data, error }` |
| `Models/Dtos/BangumiAnimeDto.cs` | Bangumi anime DTO |
| `Models/Dtos/ExternalUrlsDto.cs` | External links (Bangumi/TMDB/AniList/MAL URLs) |
| `Models/Dtos/MikanSearchDtos.cs` | Mikan search results DTO (anime list/fansub groups) |
| `Models/Dtos/SubscriptionDtos.cs` | Subscription request/response DTOs |
| `Models/Jikan/JikanModels.cs` | Jikan API response models |
| `Models/Mikan/MikanRssModels.cs` | Mikan RSS XML parsing models |
| `Models/Configuration/ApiConfiguration.cs` | TMDB/Jikan API configuration class |
| `Models/Configuration/MikanConfiguration.cs` | Mikan polling configuration class |
| `Models/Configuration/QBittorrentConfiguration.cs` | qBittorrent connection configuration class |

### Root Configuration

| File | Description |
|------|-------------|
| `Program.cs` | App startup: DI registration, middleware pipeline, database initialization |
| `appsettings.json` | Default config (log levels, API base URLs, defaults) |
| `appsettings.Development.json` | Development environment overrides |
| `appsettings.runtime.json` | Runtime config written by setup wizard (highest priority) |

---

## Frontend File Reference

### Entry & Routing

| File | Description |
|------|-------------|
| `main.tsx` | React entry point, mounts `<App />` to `#root` |
| `App.tsx` | Root component: RouterProvider, auth status check on load, ProtectedRoute |
| `index.css` | Global styles + Tailwind CSS + custom CSS classes (`.sidebar-header`, `.content-header`) |
| `config/env.ts` | Environment variable reader (API base URL, etc.) |

### State Management

| File | Description |
|------|-------------|
| `stores/useAppStores.tsx` | Zustand store, persisted to localStorage (`anime-app-storage`): language (zh/en), username, modal open state |

### Type Definitions

| File | Description |
|------|-------------|
| `types/anime.ts` | `AnimeInfo`, `AnimeListResponse`, and related anime types |
| `types/auth.ts` | `LoginRequest`, `AuthStatus`, `SetupRequest`, and auth types |
| `types/mikan.ts` | `MikanFeedItem`, `TorrentInfo`, `DownloadRequest`, and torrent types |
| `types/settings.ts` | `SettingsProfile`, `TokenStatus`, and settings types |
| `types/subscription.ts` | `Subscription`, `SubscriptionRequest`, `DownloadHistory`, and subscription types |

### API Services

| File | Description |
|------|-------------|
| `services/apiClient.ts` | Core HTTP wrapper: `authFetch()`, auto Bearer Token injection, clears token and redirects on 401 |
| `services/authApi.ts` | Auth endpoints: `login()`, `getStatus()`, `setup()`, `changeCredentials()` |
| `services/subscriptionApi.ts` | Subscription endpoints: CRUD, `toggle()`, `check()`, `getHistory()` |
| `services/mikanApi.ts` | Mikan endpoints: `search()`, `getFeed()`, `download()`, `getTorrents()`, torrent controls |
| `services/settingsApi.ts` | Settings endpoints: `getProfile()`, `saveProfile()`, `testQBittorrent()`, token management |

### Utilities

| File | Description |
|------|-------------|
| `utils/formatFileSize.ts` | Format byte count to human-readable string (KB/MB/GB) |
| `utils/torrentState.ts` | qBittorrent torrent state enum and status text/color mapping |

### Common Components

| File | Description |
|------|-------------|
| `components/common/ErrorMessage.tsx` | Error display with retry button |
| `components/common/LoadingSpinner.tsx` | Loading animation |
| `components/common/ToastContainer.tsx` | Global toast notification container |

### Icon Components

18 pure SVG icon components: `HomeIcon`, `DownloadIcon`, `SettingIcon`, `SidebarIcon`, `LanguageToggleIcon`, `LogoutIcon`, `GithubIcon`, `BellIcon`, `CheckIcon`, `CloseIcon`, `ExternalLinkIcon`, `EyeClosedIcon`, `EyeOpenIcon`, `PlayTriangleIcon`, `SearchIcon`, `ShuffleIcon`, `StarIcon`

### Auth Feature (`features/auth`)

| File | Description |
|------|-------------|
| `features/auth/components/LoginBlock.tsx` | Login form: username/password input, login button, error display, background image |

### Setup Feature (`features/setup`)

| File | Description |
|------|-------------|
| `features/setup/SetupPage.tsx` | 5-step setup wizard: Create Account → qBittorrent → TMDB → Preferences → Verification |

### Home Feature (`features/home`)

**Layout Components**

| File | Description |
|------|-------------|
| `layout/HomePage.tsx` | Main layout: Sidebar + content area + React Router outlet |
| `layout/SideBar.tsx` | Collapsible sidebar: nav links, user info, language toggle, logout |
| `layout/SideBarButton.tsx` | Reusable sidebar button: icon + label + active state |

**Content Components**

| File | Description |
|------|-------------|
| `components/HomePageContent.tsx` | Home page: fetches anime data, renders multiple AnimeInfoFlow carousels |
| `components/AnimeInfoFlow.tsx` | Horizontally scrollable anime card carousel with mouse drag support |
| `components/AnimeCard.tsx` | Single anime card: cover image, rating, title, hover effects |
| `components/AnimeDetailModal.tsx` | Anime detail modal: backdrop, bilingual description, external links, rendered via `createPortal` |
| `components/SearchPage.tsx` | Search page: Mikan search, RSS filtering, resource list, one-click download |
| `components/DownloadPage.tsx` | Download manager: qBittorrent torrent list, progress, pause/resume/delete |
| `components/MySubscriptionDownloadPage.tsx` | Subscription downloads: subscription list + per-subscription download history |
| `components/SubscriptionDownloadDetailModal.tsx` | Subscription detail modal: RSS config, download history log |
| `components/SubscriptionInfo.tsx` | Subscription info display: configuration summary |
| `components/DownloadActionButton.tsx` | Context-aware download button: shows Download / Subscribed / Cancel based on state |
| `components/DownloadEpisodeGroup.tsx` | Episode-grouped resource list |
| `components/SettingPage.tsx` | Settings page: qBittorrent, TMDB token, polling config |

---

## Backend API Reference

All APIs require JWT authentication (Bearer Token). Endpoints marked 🔓 are public.

### Auth `/api/auth`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/auth/status` | 🔓 | Check if system is initialized and if user is authenticated |
| `POST` | `/api/auth/login` | 🔓 | Login — returns JWT token |
| `POST` | `/api/auth/setup` | 🔓 | Initial setup wizard — creates first account and writes config |
| `POST` | `/api/auth/change-credentials` | 🔒 | Change username or password |
| `GET` | `/api/auth/background` | 🔓 | Get login page background image (returns file) |
| `POST` | `/api/auth/background` | 🔒 | Upload custom login background image |
| `DELETE` | `/api/auth/background` | 🔒 | Delete custom background, restore default |

### Anime Data `/api/anime`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/anime/today` | 🔒 | Get this week's airing anime (DB-first, falls back to real-time) |
| `GET` | `/api/anime/top/bangumi` | 🔒 | Bangumi score Top 10 |
| `GET` | `/api/anime/top/anilist` | 🔒 | AniList trending Top 10 |
| `GET` | `/api/anime/top/mal` | 🔒 | MyAnimeList Top 10 (via Jikan) |
| `GET` | `/api/anime/search?keyword=` | 🔒 | Search anime (Mikan primary + Bangumi supplement) |
| `GET` | `/api/anime/random` | 🔒 | Get random recommended anime list |
| `POST` | `/api/anime/batch` | 🔒 | Batch lookup anime info by Bangumi IDs from local DB |

### Subscriptions `/api/subscription`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/subscription` | 🔒 | List all subscriptions |
| `GET` | `/api/subscription/{id}` | 🔒 | Get single subscription details |
| `GET` | `/api/subscription/bangumi/{bangumiId}` | 🔒 | Find subscription by Bangumi ID |
| `POST` | `/api/subscription` | 🔒 | Create new subscription |
| `PUT` | `/api/subscription/{id}` | 🔒 | Update subscription config |
| `DELETE` | `/api/subscription/{id}` | 🔒 | Delete subscription (history retained) |
| `POST` | `/api/subscription/{id}/toggle?enabled=` | 🔒 | Enable/disable subscription |
| `POST` | `/api/subscription/{id}/check` | 🔒 | Immediately check this subscription's RSS |
| `POST` | `/api/subscription/check-all` | 🔒 | Immediately check all enabled subscriptions |
| `POST` | `/api/subscription/ensure` | 🔒 | Ensure subscription exists and is enabled (idempotent) |
| `POST` | `/api/subscription/{id}/cancel` | 🔒 | Cancel subscription (optionally delete downloaded files) |
| `GET` | `/api/subscription/{id}/history` | 🔒 | Get download history for subscription |
| `GET` | `/api/subscription/{id}/task-hashes` | 🔒 | Get qBittorrent task hashes for this subscription |
| `GET` | `/api/subscription/manual-anime` | 🔒 | List anime with manual downloads but no subscription |
| `GET` | `/api/subscription/manual-anime/{bangumiId}/history` | 🔒 | Manual download history for a specific anime |
| `GET` | `/api/subscription/manual-anime/{bangumiId}/task-hashes` | 🔒 | Task hashes for manual downloads |

### Mikan & Downloads `/api/mikan`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/mikan/search?keyword=` | 🔒 | Search Mikan for anime — returns all seasons |
| `GET` | `/api/mikan/search-entries?keyword=` | 🔒 | Search Mikan anime entries |
| `POST` | `/api/mikan/correct-bangumi-id` | 🔒 | Correct Mikan Bangumi ID mapping |
| `GET` | `/api/mikan/subgroups?mikanBangumiId=` | 🔒 | Get fansub group list for an anime |
| `GET` | `/api/mikan/feed?mikanBangumiId=&subgroupId=` | 🔒 | Get RSS torrent list (with episode normalization) |
| `GET` | `/api/mikan/filter` | 🔒 | Filter RSS items by resolution/subtitle type |
| `POST` | `/api/mikan/download` | 🔒 | Send torrent to qBittorrent |
| `GET` | `/api/mikan/torrents?hashes=` | 🔒 | Query download status of specified hashes |
| `POST` | `/api/mikan/torrents/{hash}/pause` | 🔒 | Pause specified torrent |
| `POST` | `/api/mikan/torrents/{hash}/resume` | 🔒 | Resume specified torrent |
| `DELETE` | `/api/mikan/torrents/{hash}` | 🔒 | Remove specified torrent (optionally delete files) |

### Settings `/api/settings`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/settings/tokens` | 🔒 | View token configuration status (masked) |
| `PUT` | `/api/settings/tokens` | 🔒 | Update TMDB token |
| `DELETE` | `/api/settings/tokens` | 🔒 | Delete stored TMDB token |
| `GET` | `/api/settings/profile` | 🔒 | Get full settings profile (qBittorrent/Mikan/token) |
| `PUT` | `/api/settings/profile` | 🔒 | Save settings profile |
| `POST` | `/api/settings/test/tmdb` | 🔓 | Test TMDB token validity |
| `POST` | `/api/settings/test/qbittorrent` | 🔓 | Test qBittorrent connection |
| `POST` | `/api/settings/test/mikan-polling` | 🔓 | Validate Mikan polling interval config |

### Admin `/api/admin`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/admin/prefetch/status` | 🔒 | Get pre-fetch service status |
| `POST` | `/api/admin/prefetch` | 🔒 | Manually trigger a data pre-fetch |
| `GET` | `/api/admin/prefetch/stats` | 🔒 | Database pre-fetch statistics (count/latest time) |
| `DELETE` | `/api/admin/prefetch/data` | 🔒 | Clear all pre-fetched data |

### Health `/health`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/health` | 🔓 | Service heartbeat — returns `{ status: "healthy" }` |
| `GET` | `/health/dependencies` | 🔒 | Check Bangumi/TMDB/AniList connectivity |

---

## Database Schema

```
AnimeInfo          AnimeImages        Subscriptions       DownloadHistory
──────────         ──────────         ─────────────       ───────────────
BangumiId (PK)     BangumiId (PK)     Id (PK AUTO)        Id (PK AUTO)
NameJapanese       PosterUrl          BangumiId           SubscriptionId (FK)
NameChinese        BackdropUrl        Title               TorrentUrl
NameEnglish        TmdbId             MikanBangumiId      TorrentHash (UNIQUE)
DescChinese        AniListId          SubgroupId          Title
DescEnglish        CreatedAt          SubgroupName        FileSize
Score              UpdatedAt          KeywordInclude      Status (0-4)
ImagePortrait                         KeywordExclude      DownloadSource
ImageLandscape                        IsEnabled           ErrorMessage
TmdbId                                LastCheckedAt       PublishedAt
AnilistId                             LastDownloadAt      DiscoveredAt
MikanBangumiId                        DownloadCount       DownloadedAt
Weekday                               CreatedAt
AirDate                               UpdatedAt
IsPreFetched
CreatedAt/UpdatedAt


MikanFeedCache         MikanFeedItem         MikanSubgroup
──────────────         ─────────────         ─────────────
Id (PK)                Id (PK)               MikanBangumiId (PK)
MikanBangumiId         FeedCacheId (FK)      SubgroupId (PK)
SubgroupId             Title                 SubgroupName
FetchedAt              TorrentUrl            CreatedAt
ExpiresAt              TorrentHash
                       PublishedAt
                       FileSize
                       Episode

TopAnimeCache          User
─────────────          ────
Id (PK)                Id (PK AUTO)
Source                 Username (UNIQUE)
DataJson               PasswordHash
FetchedAt              CreatedAt
ExpiresAt              UpdatedAt
```
