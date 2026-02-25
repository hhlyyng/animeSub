# 开发参考

本文档记录系统当前架构、所有源文件作用及后端完整 API 列表，供开发者参考。

---

## 系统架构图

### 整体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                         用户浏览器                                │
│                                                                  │
│  React 19 + TypeScript + Vite + Tailwind CSS + Zustand          │
│                                                                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────┐  │
│  │ 首页放送  │  │ 搜索/下载 │  │  订阅管理 │  │   系统设置   │  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └──────┬───────┘  │
│       └─────────────┴──────────────┴────────────────┘          │
│                          authFetch()                            │
│                     (Bearer JWT + 401 重定向)                   │
└─────────────────────────────┬───────────────────────────────────┘
                              │ HTTP / JSON
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     ASP.NET Core 9 后端                          │
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
│  │   AnimeRepository   │  │  AnimePreFetchService (每日03:00)  │  │
│  │ SubscriptionRepo    │  │  RssPollingService   (每30分钟)    │  │
│  └──────────┬──────────┘  │  DownloadProgressSync (实时同步)   │  │
│             │              │  AnimeTitleBackfill  (补全标题)    │  │
│  ┌──────────▼──────────┐  │  MikanFeedCleanup    (清理缓存)   │  │
│  │   SQLite Database   │  └───────────────────────────────────┘  │
│  │   (anime.db)        │                                          │
│  │                     │                                          │
│  │  AnimeInfo          │                                          │
│  │  AnimeImages        │                                          │
│  │  Subscriptions      │                                          │
│  │  DownloadHistory    │                                          │
│  │  DailyScheduleCache │                                          │
│  │  MikanFeedCache     │                                          │
│  │  MikanFeedItem      │                                          │
│  │  MikanSubgroup      │                                          │
│  │  TopAnimeCache      │                                          │
│  │  User               │                                          │
│  └─────────────────────┘                                          │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┼──────────────────────┐
          ▼                   ▼                        ▼
┌─────────────────┐  ┌──────────────────┐  ┌─────────────────────┐
│   Bangumi API   │  │    TMDB API      │  │   AniList GraphQL   │
│ api.bgm.tv/v0   │  │ api.tmdb.org/3   │  │  graphql.anilist.co │
│                 │  │                  │  │                     │
│ 每日放送时间表  │  │ 英文元数据       │  │ 英文标题/简介       │
│ 评分排行榜      │  │ 横版背景图       │  │ 趋势排行榜          │
│ 动漫详情        │  │ 智能季度匹配     │  │ (备用来源)          │
└─────────────────┘  └──────────────────┘  └─────────────────────┘
          │
          ├─────────────────────────┐
          ▼                         ▼
┌─────────────────┐       ┌──────────────────────┐
│   Jikan API     │       │     Mikan RSS         │
│ api.jikan.moe   │       │   mikanani.me         │
│                 │       │                       │
│ MAL Top 10      │       │ 番剧 RSS 种子列表     │
│ (MyAnimeList)   │       │ 字幕组分组信息        │
└─────────────────┘       └──────────┬────────────┘
                                     │
                          ┌──────────▼────────────┐
                          │    qBittorrent WebUI  │
                          │    localhost:8080      │
                          │                       │
                          │  种子添加/暂停/恢复   │
                          │  下载进度查询         │
                          └───────────────────────┘
```

### 数据聚合流程

```
GET /api/anime/today
        │
        ▼
AnimeAggregationService
        │
        ├─① 查询 SQLite (GetAnimesByWeekdayAsync)
        │         │
        │    有预取数据? ──是──► 返回 DB 数据 (<100ms)
        │         │
        │         否
        │         ▼
        ├─② Bangumi API
        │   GET /v0/calendar → 获取本周放送
        │   每部番剧: id, name, air_date, score, 封面图
        │         │
        ├─③ TMDB API (并发)
        │   搜索番剧名 → 智能匹配 (Animation优先/JP优先/年份过滤)
        │   GET /3/search/tv → GET /3/tv/{id}/season/{n}
        │   提取: 英文标题, 英文简介, 横版背景图
        │         │
        └─④ AniList GraphQL (备用)
            query { Media } → 英文标题/简介 (Bangumi有数据时跳过)
                    │
                    ▼
             AnimeInfo[] → 返回前端 → 缓存至 sessionStorage
```

### 预取架构

```
AnimePreFetchService (BackgroundService)
        │
        ├── 启动时: RunOnStartup=true → 立即执行一次
        │
        └── 定时: 每天 ScheduleHour (默认凌晨3点)
                  │
                  ├─ GetFullCalendarAsync() → 获取全周 Bangumi 数据
                  ├─ 并发聚合 (MaxConcurrency=3)
                  │   每部番剧 → TMDB + AniList 丰富元数据
                  └─ 批量写入 SQLite
                     设置 IsPreFetched=true
```

### 订阅下载流程

```
RssPollingService (每30分钟)
        │
        ├─ 检查 EnablePolling 配置
        │
        └─ 遍历所有 IsEnabled=true 的订阅
                  │
                  ▼
          MikanClient.GetFeedAsync(mikanBangumiId, subgroupId)
                  │
                  ▼
          解析 RSS XML → MikanFeedItem[]
                  │
                  ▼
          过滤流程:
          ┌─────────────────────────────────────┐
          │ 1. TorrentHash ∈ DownloadHistory?  │
          │    是 → 跳过 (已下载)              │
          │    否 → 继续                        │
          │ 2. 字幕组 ID 匹配?                 │
          │    不匹配 → 跳过                   │
          │ 3. KeywordInclude 全部命中?        │
          │    否 → 跳过                        │
          │ 4. KeywordExclude 任一命中?        │
          │    是 → 跳过                        │
          └─────────────────────────────────────┘
                  │
                  ▼ (通过过滤)
          QBittorrentService.AddTorrentAsync()
                  │
                  ▼
          写入 DownloadHistory (Status=Downloading)
          更新 Subscription.LastDownloadAt
```

---

## 后端文件清单

### Controllers

| 文件 | 路由前缀 | 说明 |
|------|----------|------|
| `Controllers/AnimeController.cs` | `/api/anime` | 动漫数据聚合：今日放送、Top10、搜索、随机推荐、批量查询 |
| `Controllers/AuthController.cs` | `/api/auth` | 认证系统：登录、注册、安装向导、修改密码、背景图管理 |
| `Controllers/SubscriptionController.cs` | `/api/subscription` | 订阅管理：CRUD、手动检查、下载历史、任务 Hash 查询 |
| `Controllers/MikanController.cs` | `/api/mikan` | Mikan 操作：RSS 搜索/解析/过滤、种子下载、qBittorrent 控制 |
| `Controllers/AdminController.cs` | `/api/admin` | 管理接口：预取状态、手动触发预取、数据库统计、清空数据 |
| `Controllers/SettingsController.cs` | `/api/settings` | 设置管理：Token 读写、用户配置、连接测试 |
| `Controllers/HealthController.cs` | `/health` | 健康检查：基础心跳、外部 API 依赖检查 |

### Services/Interfaces

| 文件 | 说明 |
|------|------|
| `IAnimeAggregationService.cs` | 数据聚合服务接口：定义今日放送、Top10、搜索等聚合方法 |
| `IAnimePoolService.cs` | 随机推荐池接口：获取随机番剧列表 |
| `IAniListClient.cs` | AniList GraphQL 客户端接口 |
| `IBangumiClient.cs` | Bangumi HTTP 客户端接口：放送、排行、详情 |
| `IJikanClient.cs` | Jikan（MAL）HTTP 客户端接口 |
| `IMikanClient.cs` | Mikan RSS 客户端接口：搜索、解析、字幕组 |
| `IQBittorrentService.cs` | qBittorrent WebUI 接口：添加/暂停/恢复/删除/查询 |
| `ISubscriptionService.cs` | 订阅业务逻辑接口 |
| `ITMDBClient.cs` | TMDB API 客户端接口：搜索、季度图片 |
| `IAuthService.cs` | 认证服务接口：登录验证、JWT 生成、密码修改 |
| `ITorrentTitleParser.cs` | 种子标题解析接口：提取集数、分辨率、字幕组 |

### Services/Implementations

| 文件 | 说明 |
|------|------|
| `AnimeAggregationService.cs` | 核心聚合服务：协调 Bangumi→TMDB→AniList 数据，DB 优先读取 |
| `AnimePoolService.cs` | 随机推荐池：内存缓存 + SQLite L2 备选，启动时预热 |
| `AnimePoolBuilderService.cs` | 推荐池构建器：从 AniList 获取热门番剧填充推荐池 |
| `AniListClient.cs` | AniList GraphQL 客户端实现：trending 查询、标题/简介提取 |
| `BangumiClient.cs` | Bangumi API 实现：`/v0/calendar`、`/v0/subjects` 排行 |
| `JikanClient.cs` | Jikan API 实现：`/v4/top/anime` MAL 排行 |
| `MikanClient.cs` | Mikan 实现：HtmlAgilityPack 解析番剧页、XML 解析 RSS |
| `QBittorrentService.cs` | qBittorrent WebUI API 封装：登录 cookie 管理、种子操作 |
| `SubscriptionService.cs` | 订阅业务逻辑：CRUD、RSS 检查、关键词过滤 |
| `TMDBClient.cs` | TMDB 实现：TV 搜索 + Animation 优先匹配 + 季度图片 |

### Services/Background

| 文件 | 触发时机 | 说明 |
|------|----------|------|
| `AnimePreFetchService.cs` | 启动时 + 每日凌晨3点 | 批量预取全周放送数据写入 SQLite |
| `RssPollingService.cs` | 启动30秒后 + 每30分钟 | 检查所有订阅 RSS，自动下载新种子 |
| `DownloadProgressSyncService.cs` | 后台持续运行 | 定期同步 qBittorrent 下载进度到 DownloadHistory |
| `AnimeTitleBackfillService.cs` | 启动时 | 补全数据库中缺失的中文标题（Bangumi fallback） |
| `MikanFeedSubgroupCleanupService.cs` | 定期 | 清理过期的 Mikan Feed 缓存数据 |

### Services/Repositories

| 文件 | 说明 |
|------|------|
| `IAnimeRepository.cs` / `AnimeRepository.cs` | 动漫数据访问：按星期查询、批量写入、Top 缓存管理 |
| `ISubscriptionRepository.cs` / `SubscriptionRepository.cs` | 订阅数据访问：CRUD、按 Bangumi ID 查询、历史记录 |

### Services（工具类）

| 文件 | 说明 |
|------|------|
| `ApiClientBase.cs` | HTTP 客户端基类：统一错误处理、日志、BaseAddress 管理 |
| `AnimeCacheService.cs` | 两级缓存：Memory Cache（L1）+ SQLite（L2） |
| `AuthService.cs` | JWT 生成（HMAC-SHA256）、bcrypt 密码哈希、用户验证 |
| `HealthCheckService.cs` | 外部 API 依赖检查（Bangumi/TMDB/AniList 连通性） |
| `TokenStorageService.cs` | TMDB Token 加密存储（ASP.NET Data Protection API） |
| `ResilienceService.cs` | Polly 重试策略 + 熔断器，保护外部 API 调用 |

### Services/Utilities

| 文件 | 说明 |
|------|------|
| `Utilities/TitleCleaner.cs` | 动漫标题规范化：去除噪声字符、统一格式 |
| `Utilities/TitleLanguageResolver.cs` | 标题语言检测：判断中/日/英文 |
| `Utils/TorrentHashHelper.cs` | 种子 Hash 计算辅助 |

### Services/Exceptions

| 文件 | 说明 |
|------|------|
| `ApiException.cs` | 通用 API 异常基类 |
| `BangumiApiException.cs` | Bangumi API 专属异常 |
| `ExternalApiException.cs` | 外部 API 调用失败异常 |
| `InvalidCredentialsException.cs` | 认证凭据无效异常 |
| `QBittorrentUnavailableException.cs` | qBittorrent 连接不可用异常 |
| `ValidationException.cs` | 请求数据验证失败异常 |

### Data（数据层）

| 文件 | 说明 |
|------|------|
| `Data/AnimeDbContext.cs` | EF Core SQLite 上下文，定义所有 DbSet |
| `Data/DbSchemaPatcher.cs` | 轻量数据库 Schema 迁移（补丁模式） |
| `Data/Entities/AnimeInfoEntity.cs` | 聚合动漫信息（标题/评分/图片/链接/星期/预取状态） |
| `Data/Entities/AnimeImagesEntity.cs` | 图片缓存（竖版封面 + 横版背景图） |
| `Data/Entities/DailyScheduleCacheEntity.cs` | 每日放送缓存（日期 → Bangumi ID 数组） |
| `Data/Entities/DownloadHistoryEntity.cs` | 下载历史（Hash 去重、状态追踪） |
| `Data/Entities/DownloadSource.cs` | 枚举：Subscription / Manual |
| `Data/Entities/MikanFeedCacheEntity.cs` | Mikan RSS Feed 缓存 |
| `Data/Entities/MikanFeedItemEntity.cs` | 单条 Feed 记录（种子标题/URL/Hash） |
| `Data/Entities/MikanSubgroupEntity.cs` | 字幕组 ID ↔ 名称映射缓存 |
| `Data/Entities/SubscriptionEntity.cs` | 订阅配置（番剧/字幕组/关键词/状态） |
| `Data/Entities/TopAnimeCacheEntity.cs` | Top 10 排行缓存（来源/数据/过期时间） |
| `Data/Entities/UserEntity.cs` | 用户账户（用户名/bcrypt 密码哈希） |

### Middleware

| 文件 | 说明 |
|------|------|
| `Middleware/CorrelationIdMiddleware.cs` | 为每个请求添加 `X-Correlation-ID` 请求追踪 ID |
| `Middleware/ExceptionHandlerMiddleware.cs` | 全局异常捕获，返回标准化错误 JSON |
| `Middleware/PerformanceMonitoringMiddleware.cs` | 记录请求耗时，添加 `X-Response-Time-Ms` 响应头 |
| `Middleware/RequestResponseLoggingMiddleware.cs` | 记录请求/响应日志（路径/状态码/耗时） |

### Models

| 文件/目录 | 说明 |
|-----------|------|
| `Models/AnimeResponse.cs` | 动漫列表响应（含数据来源标识：DB/API） |
| `Models/PreFetchStatus.cs` | 预取服务状态（运行中/上次执行时间/统计） |
| `Models/ErrorResponse.cs` | 标准错误响应格式 |
| `Models/TMDBAnimeInfo.cs` | TMDB 搜索结果映射模型 |
| `Models/AniListAnimeInfo.cs` | AniList GraphQL 响应映射 |
| `Models/Dtos/AnimeInfoDto.cs` | 聚合动漫信息 DTO（前端展示用） |
| `Models/Dtos/AnimeImagesDto.cs` | 图片 DTO |
| `Models/Dtos/ApiResponseDto.cs` | 统一 API 响应包装 `{ success, data, error }` |
| `Models/Dtos/BangumiAnimeDto.cs` | Bangumi 番剧 DTO |
| `Models/Dtos/ExternalUrlsDto.cs` | 外部链接（Bangumi/TMDB/AniList/MAL URL） |
| `Models/Dtos/MikanSearchDtos.cs` | Mikan 搜索结果 DTO（番剧列表/字幕组） |
| `Models/Dtos/SubscriptionDtos.cs` | 订阅请求/响应 DTO |
| `Models/Jikan/JikanModels.cs` | Jikan API 响应模型 |
| `Models/Mikan/MikanRssModels.cs` | Mikan RSS XML 解析模型 |
| `Models/Configuration/ApiConfiguration.cs` | TMDB/Jikan API 配置类 |
| `Models/Configuration/MikanConfiguration.cs` | Mikan 轮询配置类 |
| `Models/Configuration/QBittorrentConfiguration.cs` | qBittorrent 连接配置类 |

### 根目录配置文件

| 文件 | 说明 |
|------|------|
| `Program.cs` | 应用启动：DI 注册、中间件管道、数据库初始化 |
| `appsettings.json` | 默认配置（日志级别、API 地址、默认值） |
| `appsettings.Development.json` | 开发环境覆盖配置 |
| `appsettings.runtime.json` | 安装向导写入的运行时配置（优先级最高） |

---

## 前端文件清单

### 入口 & 路由

| 文件 | 说明 |
|------|------|
| `main.tsx` | React 应用入口，挂载 `<App />` 到 `#root` |
| `App.tsx` | 根组件：路由配置（RouterProvider）、认证状态检查、ProtectedRoute |
| `index.css` | 全局样式 + Tailwind CSS + 自定义 CSS 类（`.sidebar-header`、`.content-header`） |
| `config/env.ts` | 环境变量读取（API base URL 等） |

### State Management

| 文件 | 说明 |
|------|------|
| `stores/useAppStores.tsx` | Zustand store，持久化到 localStorage (`anime-app-storage`)：语言(zh/en)、用户名、Modal 开关状态 |

### Type Definitions

| 文件 | 说明 |
|------|------|
| `types/anime.ts` | `AnimeInfo`、`AnimeListResponse` 等动漫相关类型 |
| `types/auth.ts` | `LoginRequest`、`AuthStatus`、`SetupRequest` 等认证类型 |
| `types/mikan.ts` | `MikanFeedItem`、`TorrentInfo`、`DownloadRequest` 等下载类型 |
| `types/settings.ts` | `SettingsProfile`、`TokenStatus` 等设置类型 |
| `types/subscription.ts` | `Subscription`、`SubscriptionRequest`、`DownloadHistory` 等订阅类型 |

### API Services

| 文件 | 说明 |
|------|------|
| `services/apiClient.ts` | 核心 HTTP 封装：`authFetch()`，自动附加 Bearer Token，401 时清除 Token 并跳转登录 |
| `services/authApi.ts` | 认证接口：`login()`、`getStatus()`、`setup()`、`changeCredentials()` |
| `services/subscriptionApi.ts` | 订阅接口：CRUD、`toggle()`、`check()`、`getHistory()` |
| `services/mikanApi.ts` | Mikan 接口：`search()`、`getFeed()`、`download()`、`getTorrents()`、种子控制 |
| `services/settingsApi.ts` | 设置接口：`getProfile()`、`saveProfile()`、`testQBittorrent()`、Token 管理 |

### Utility Functions

| 文件 | 说明 |
|------|------|
| `utils/formatFileSize.ts` | 将字节数格式化为人类可读字符串（KB/MB/GB） |
| `utils/torrentState.ts` | qBittorrent 种子状态枚举及状态文字/颜色映射 |

### 通用组件 (components/common)

| 文件 | 说明 |
|------|------|
| `common/ErrorMessage.tsx` | 错误信息展示组件，含重试按钮 |
| `common/LoadingSpinner.tsx` | 加载动画组件 |
| `common/ToastContainer.tsx` | Toast 通知容器（全局消息提示） |

### 图标组件 (components/icons)

18 个纯 SVG 图标组件，包括：`HomeIcon`、`DownloadIcon`、`SettingIcon`、`SidebarIcon`、`LanguageToggleIcon`、`LogoutIcon`、`GithubIcon`、`BellIcon`、`CheckIcon`、`CloseIcon`、`ExternalLinkIcon`、`EyeClosedIcon`、`EyeOpenIcon`、`PlayTriangleIcon`、`SearchIcon`、`ShuffleIcon`、`StarIcon`

### 认证功能 (features/auth)

| 文件 | 说明 |
|------|------|
| `features/auth/components/LoginBlock.tsx` | 登录表单：用户名/密码输入、登录按钮、错误提示、背景图展示 |

### 安装向导功能 (features/setup)

| 文件 | 说明 |
|------|------|
| `features/setup/SetupPage.tsx` | 5 步安装向导：创建账户→qBittorrent→TMDB→偏好设置→验证 |

### 主功能 (features/home)

**布局组件**

| 文件 | 说明 |
|------|------|
| `layout/HomePage.tsx` | 主布局：Sidebar + 内容区 + React Router 路由出口 |
| `layout/SideBar.tsx` | 可折叠侧边栏：导航链接、用户信息、语言切换、退出登录 |
| `layout/SideBarButton.tsx` | 侧边栏按钮复用组件：图标+标签+激活状态 |

**内容组件**

| 文件 | 说明 |
|------|------|
| `components/HomePageContent.tsx` | 今日放送主页：从 API 获取数据、展示多个 AnimeInfoFlow 轮播 |
| `components/AnimeInfoFlow.tsx` | 横向可滚动番剧卡片轮播，支持鼠标拖拽 |
| `components/AnimeCard.tsx` | 单个番剧卡片：封面图、评分、标题、hover 效果 |
| `components/AnimeDetailModal.tsx` | 番剧详情弹窗：背景图、双语简介、外部链接，`createPortal` 渲染 |
| `components/SearchPage.tsx` | 搜索页面：Mikan 搜索、RSS 过滤、资源列表、一键下载 |
| `components/DownloadPage.tsx` | 下载管理页：qBittorrent 种子列表、进度、暂停/恢复/删除 |
| `components/MySubscriptionDownloadPage.tsx` | 订阅下载页：订阅列表 + 各订阅下载历史 |
| `components/SubscriptionDownloadDetailModal.tsx` | 订阅详情弹窗：RSS 配置、下载历史记录 |
| `components/SubscriptionInfo.tsx` | 订阅信息展示：订阅配置摘要 |
| `components/DownloadActionButton.tsx` | 下载动作按钮：根据状态显示不同按钮（下载/已订阅/取消） |
| `components/DownloadEpisodeGroup.tsx` | 按集数分组的资源列表 |
| `components/SettingPage.tsx` | 设置页面：qBittorrent、TMDB Token、轮询配置 |

---

## 后端 API 参考

所有 API 均需 JWT 认证（Bearer Token），标注 `🔓` 的端点除外。

### 认证 `/api/auth`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/api/auth/status` | 🔓 | 查询系统是否已初始化、当前是否已登录 |
| `POST` | `/api/auth/login` | 🔓 | 用户登录，返回 JWT Token |
| `POST` | `/api/auth/setup` | 🔓 | 首次安装向导，创建初始账户和配置 |
| `POST` | `/api/auth/change-credentials` | 🔒 | 修改用户名或密码 |
| `GET` | `/api/auth/background` | 🔓 | 获取登录页背景图（返回图片文件） |
| `POST` | `/api/auth/background` | 🔒 | 上传自定义登录背景图 |
| `DELETE` | `/api/auth/background` | 🔒 | 删除自定义背景图，恢复默认 |

### 动漫数据 `/api/anime`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/api/anime/today` | 🔒 | 获取本周放送番剧（DB优先，回退实时聚合） |
| `GET` | `/api/anime/top/bangumi` | 🔒 | Bangumi 评分 Top 10 |
| `GET` | `/api/anime/top/anilist` | 🔒 | AniList 趋势 Top 10 |
| `GET` | `/api/anime/top/mal` | 🔒 | MyAnimeList Top 10（via Jikan） |
| `GET` | `/api/anime/search?keyword=` | 🔒 | 搜索动漫（Mikan 主源 + Bangumi 补充） |
| `GET` | `/api/anime/random` | 🔒 | 获取随机推荐番剧列表 |
| `POST` | `/api/anime/batch` | 🔒 | 批量查询 Bangumi ID 对应的番剧信息（从本地 DB） |

### 订阅管理 `/api/subscription`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/api/subscription` | 🔒 | 获取所有订阅列表 |
| `GET` | `/api/subscription/{id}` | 🔒 | 获取单个订阅详情 |
| `GET` | `/api/subscription/bangumi/{bangumiId}` | 🔒 | 按 Bangumi ID 查询订阅 |
| `POST` | `/api/subscription` | 🔒 | 创建新订阅 |
| `PUT` | `/api/subscription/{id}` | 🔒 | 更新订阅配置 |
| `DELETE` | `/api/subscription/{id}` | 🔒 | 删除订阅（保留下载历史） |
| `POST` | `/api/subscription/{id}/toggle?enabled=` | 🔒 | 启用/禁用订阅 |
| `POST` | `/api/subscription/{id}/check` | 🔒 | 立即手动检查该订阅 RSS |
| `POST` | `/api/subscription/check-all` | 🔒 | 立即检查所有已启用订阅 |
| `POST` | `/api/subscription/ensure` | 🔒 | 确保订阅存在且启用（幂等，不存在则创建） |
| `POST` | `/api/subscription/{id}/cancel` | 🔒 | 取消订阅（可选删除已下载文件） |
| `GET` | `/api/subscription/{id}/history` | 🔒 | 获取订阅下载历史 |
| `GET` | `/api/subscription/{id}/task-hashes` | 🔒 | 获取订阅关联的 qBittorrent 任务 Hash |
| `GET` | `/api/subscription/manual-anime` | 🔒 | 获取有手动下载但无订阅的番剧列表 |
| `GET` | `/api/subscription/manual-anime/{bangumiId}/history` | 🔒 | 获取番剧手动下载历史 |
| `GET` | `/api/subscription/manual-anime/{bangumiId}/task-hashes` | 🔒 | 获取手动下载的任务 Hash |

### Mikan & 下载 `/api/mikan`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/api/mikan/search?keyword=` | 🔒 | 在 Mikan 搜索番剧，返回所有季度列表 |
| `GET` | `/api/mikan/search-entries?keyword=` | 🔒 | 搜索 Mikan 番剧条目 |
| `POST` | `/api/mikan/correct-bangumi-id` | 🔒 | 修正 Mikan Bangumi ID 映射 |
| `GET` | `/api/mikan/subgroups?mikanBangumiId=` | 🔒 | 获取番剧的字幕组列表（名称/ID） |
| `GET` | `/api/mikan/feed?mikanBangumiId=&subgroupId=` | 🔒 | 获取番剧 RSS 资源列表（含集数归一化） |
| `GET` | `/api/mikan/filter` | 🔒 | 按分辨率/字幕类型过滤 RSS 列表 |
| `POST` | `/api/mikan/download` | 🔒 | 发送种子到 qBittorrent 下载 |
| `GET` | `/api/mikan/torrents?hashes=` | 🔒 | 查询指定 Hash 的 qBittorrent 下载状态 |
| `POST` | `/api/mikan/torrents/{hash}/pause` | 🔒 | 暂停指定种子 |
| `POST` | `/api/mikan/torrents/{hash}/resume` | 🔒 | 恢复指定种子 |
| `DELETE` | `/api/mikan/torrents/{hash}` | 🔒 | 删除指定种子（可选删文件） |

### 设置 `/api/settings`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/api/settings/tokens` | 🔒 | 查看 Token 配置状态（返回脱敏信息） |
| `PUT` | `/api/settings/tokens` | 🔒 | 更新 TMDB Token |
| `DELETE` | `/api/settings/tokens` | 🔒 | 删除已存储的 TMDB Token |
| `GET` | `/api/settings/profile` | 🔒 | 获取设置页完整配置（qBittorrent/Mikan/Token） |
| `PUT` | `/api/settings/profile` | 🔒 | 保存设置页配置 |
| `POST` | `/api/settings/test/tmdb` | 🔓 | 测试 TMDB Token 有效性 |
| `POST` | `/api/settings/test/qbittorrent` | 🔓 | 测试 qBittorrent 连接 |
| `POST` | `/api/settings/test/mikan-polling` | 🔓 | 验证 Mikan 轮询间隔配置 |

### 管理 `/api/admin`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/api/admin/prefetch/status` | 🔒 | 获取预取服务运行状态 |
| `POST` | `/api/admin/prefetch` | 🔒 | 手动触发一次数据预取 |
| `GET` | `/api/admin/prefetch/stats` | 🔒 | 获取数据库预取统计（条数/最新时间） |
| `DELETE` | `/api/admin/prefetch/data` | 🔒 | 清空所有预取数据 |

### 健康检查 `/health`

| Method | 路径 | 认证 | 说明 |
|--------|------|------|------|
| `GET` | `/health` | 🔓 | 服务心跳检查，返回 `{ status: "healthy" }` |
| `GET` | `/health/dependencies` | 🔒 | 检查 Bangumi/TMDB/AniList 连通性 |

---

## 数据库 Schema

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
