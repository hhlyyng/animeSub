# Changelog

项目改动记录，遵循 React Best Practices 进行代码重构。

---

## 2026-02-04

### 预取架构重构

#### 改动概述

将后端从实时 API 聚合架构改为预取 + 增量更新架构，将响应时间从 3-10 秒优化到 <100ms。

#### 架构对比

| 对比项 | 之前 (实时聚合) | 之后 (预取架构) |
|--------|----------------|----------------|
| 响应时间 | 3-10 秒 | <100ms |
| API 调用 | 每次请求调用多个 API | 仅在凌晨批量调用 |
| 数据来源 | Bangumi → TMDB → AniList | SQLite DB (预取) + 实时补充 |
| 可靠性 | 依赖外部 API 可用性 | 数据库优先，API 故障不影响 |

#### 数据流程

```
前端: GET /api/anime/today (无需改动)
         ↓
后端 AnimeAggregationService:
    ├── 有预取数据? → 返回 DB 数据 + 检查新番增量
    └── 无预取数据? → 实时 API 聚合
         ↓
返回统一的 AnimeListResponse
```

#### 新增文件

| 文件 | 说明 |
|------|------|
| `backend/Services/Background/AnimePreFetchService.cs` | 定时预取后台服务 |
| `backend/Models/PreFetchStatus.cs` | 预取服务状态模型 |
| `backend/Controllers/AdminController.cs` | 管理 API 控制器 |

#### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `backend/Data/Entities/AnimeInfoEntity.cs` | 扩展字段存储完整聚合数据 |
| `backend/Services/Implementations/AnimeAggregationService.cs` | 重构为 DB 优先读取 |
| `backend/Services/Repositories/IAnimeRepository.cs` | 新增批量查询和清理方法 |
| `backend/Services/Repositories/AnimeRepository.cs` | 实现新方法 |
| `backend/Services/Interfaces/IBangumiClient.cs` | 新增 `GetFullCalendarAsync()` |
| `backend/Services/Implementations/BangumiClient.cs` | 实现完整周历获取 |
| `backend/Models/AnimeResponse.cs` | 新增 `DataSource.Database` 枚举值 |
| `backend/Models/Configuration/ApiConfiguration.cs` | 新增 `PreFetchConfig` |
| `backend/appsettings.json` | 添加 PreFetch 配置节 |
| `backend/Program.cs` | 注册预取后台服务 |

#### 管理 API 端点

| Method | Endpoint | 说明 |
|--------|----------|------|
| GET | `/api/admin/prefetch/status` | 获取预取服务状态 |
| POST | `/api/admin/prefetch` | 手动触发预取 |
| GET | `/api/admin/prefetch/stats` | 获取数据库统计 |
| DELETE | `/api/admin/prefetch/data` | 清空预取数据 |

#### 配置项 (appsettings.json)

```json
{
  "PreFetch": {
    "Enabled": true,
    "ScheduleHour": 3,
    "RunOnStartup": true,
    "MaxConcurrency": 3,
    "BangumiToken": "",
    "TmdbToken": ""
  }
}
```

---

### TMDB API 优化

#### 改动概述

优化 TMDB API 匹配逻辑，解决三个问题：中文简介备选、动画与真人剧区分、多季度匹配。

#### Phase 1: 中文简介备选

当 Bangumi 中文简介为空时，使用 TMDB 中文翻译作为备选。

**修改文件**: `backend/Services/Implementations/AnimeAggregationService.cs`

```csharp
// 之前
ChDesc = chDesc,

// 之后
ChDesc = !string.IsNullOrEmpty(chDesc)
    ? chDesc
    : (tmdbResult?.ChineseSummary ?? ""),
```

#### Phase 2: 动画优先匹配

TMDB 搜索结果优先选择 Animation 类型 (genre_id=16)，进一步优先选择日本来源 (origin_country=JP)。

**修改文件**: `backend/Services/Implementations/TMDBClient.cs`

**匹配优先级**:
1. Animation (16) + Japanese (JP) - 完美匹配
2. Animation (16) - 次优匹配
3. 第一个结果 - 回退

```csharp
// 遍历结果找到最佳匹配
foreach (var item in results.EnumerateArray())
{
    // 检查 genre_ids 是否包含 16 (Animation)
    // 检查 origin_country 是否包含 JP
    if (isAnimation && isJapanese) { bestMatch = item; break; }
    if (isAnimation && bestMatch == null) { bestMatch = item; }
}
var first = bestMatch ?? results[0];
```

#### Phase 3: 多季度匹配

使用 Bangumi 播出日期的年份过滤 TMDB 搜索，获取对应季度的专属图片。

**修改文件**:
| 文件 | 修改内容 |
|------|----------|
| `backend/Services/Interfaces/ITMDBClient.cs` | 扩展方法签名添加 `airDate` 参数 |
| `backend/Services/Implementations/TMDBClient.cs` | 年份过滤 + 季度图片获取 |
| `backend/Services/Implementations/AnimeAggregationService.cs` | 从 Bangumi 获取并传递 `airDate` |

```csharp
// 接口扩展
Task<TMDBAnimeInfo?> GetAnimeSummaryAndBackdropAsync(string title, string? airDate = null);

// 搜索 URL 添加年份过滤
if (year.HasValue)
    url += $"&first_air_date_year={year}";

// 获取 TV 详情匹配季度
if (seasonAirDate.StartsWith(year.ToString()))
{
    // 找到匹配季度，获取季度图片
}
```

#### Phase 4: 单元测试

新增 `backend.Tests/Unit/Services/TMDBClientTests.cs`，包含 7 个测试用例：

| 测试 | 描述 |
|------|------|
| `WithMixedResults_SelectsAnime` | 混合结果中选择动画 |
| `WithMultipleAnime_SelectsJapanese` | 多个动画中优先日本 |
| `NoAnimeFound_FallsBackToFirst` | 无动画时回退第一个 |
| `WithAirDate_IncludesYearInSearch` | 有日期时包含年份过滤 |
| `WithoutAirDate_NoYearFilter` | 无日期时不过滤 |
| `NoResults_ReturnsDefaultInfo` | 无结果返回默认值 |
| `NoToken_ReturnsNull` | 无 Token 返回 null |

同时扩展 `TestDataFactory.cs` 添加 TMDB 测试数据工厂方法。

#### Git 提交记录

```
081a694 feat(backend): optimize TMDB API matching for anime
0b32309 feat(backend): add multi-season matching with air date filtering
a4131d7 test(backend): add unit tests for TMDB client improvements
```

---

### Mikan RSS 订阅系统实现

#### 改动概述

实现完整的 Mikan RSS 订阅功能，支持用户订阅番剧并自动下载新资源到 qBittorrent。

#### 新增功能

| 功能 | 说明 |
|------|------|
| 订阅管理 | 创建、编辑、删除番剧订阅 |
| RSS 轮询 | 后台定时检查新资源 (可配置间隔) |
| 关键词过滤 | 支持包含/排除关键词筛选 |
| 字幕组筛选 | 可指定特定字幕组 |
| 自动下载 | 新资源自动推送到 qBittorrent |
| 下载历史 | 记录所有下载，防止重复 |

#### 新增文件

**数据层**
| 文件 | 说明 |
|------|------|
| `backend/Data/Entities/SubscriptionEntity.cs` | 订阅实体 |
| `backend/Data/Entities/DownloadHistoryEntity.cs` | 下载历史实体 |
| `backend/Services/Repositories/ISubscriptionRepository.cs` | 仓储接口 |
| `backend/Services/Repositories/SubscriptionRepository.cs` | 仓储实现 |

**配置**
| 文件 | 说明 |
|------|------|
| `backend/Models/Configuration/MikanConfiguration.cs` | Mikan 配置 |
| `backend/Models/Configuration/QBittorrentConfiguration.cs` | qBittorrent 配置 |

**API 客户端**
| 文件 | 说明 |
|------|------|
| `backend/Models/Mikan/MikanRssModels.cs` | RSS 数据模型 |
| `backend/Services/Interfaces/IMikanClient.cs` | Mikan 客户端接口 |
| `backend/Services/Implementations/MikanClient.cs` | RSS 解析实现 |

**业务逻辑**
| 文件 | 说明 |
|------|------|
| `backend/Models/Dtos/SubscriptionDtos.cs` | 请求/响应 DTO |
| `backend/Services/Interfaces/ISubscriptionService.cs` | 服务接口 |
| `backend/Services/Implementations/SubscriptionService.cs` | 服务实现 |

**控制器和后台服务**
| 文件 | 说明 |
|------|------|
| `backend/Controllers/SubscriptionController.cs` | REST API |
| `backend/Services/Background/RssPollingService.cs` | 后台轮询服务 |

#### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `backend/Data/AnimeDbContext.cs` | 添加 Subscriptions 和 DownloadHistory DbSet |
| `backend/Services/Interfaces/IQBittorrentService.cs` | 扩展接口方法 |
| `backend/Services/Implementations/QBittorrentService.cs` | 完整实现 |
| `backend/appsettings.json` | 添加 Mikan 和 QBittorrent 配置 |
| `backend/Program.cs` | 注册新服务 |

#### 数据库表结构

**Subscriptions 表**
```
Id              INT PRIMARY KEY AUTO_INCREMENT
BangumiId       INT NOT NULL           -- Bangumi 番剧 ID
Title           NVARCHAR NOT NULL      -- 番剧标题
MikanBangumiId  NVARCHAR NOT NULL      -- Mikan 番剧 ID
SubgroupId      NVARCHAR NULL          -- 字幕组 ID
SubgroupName    NVARCHAR NULL          -- 字幕组名称
KeywordInclude  NVARCHAR NULL          -- 包含关键词 (逗号分隔)
KeywordExclude  NVARCHAR NULL          -- 排除关键词 (逗号分隔)
IsEnabled       BIT DEFAULT 1          -- 是否启用
LastCheckedAt   DATETIME NULL          -- 上次检查时间
LastDownloadAt  DATETIME NULL          -- 上次下载时间
DownloadCount   INT DEFAULT 0          -- 已下载集数
CreatedAt       DATETIME NOT NULL
UpdatedAt       DATETIME NOT NULL
```

**DownloadHistory 表**
```
Id              INT PRIMARY KEY AUTO_INCREMENT
SubscriptionId  INT NOT NULL           -- 关联订阅
TorrentUrl      NVARCHAR NOT NULL      -- 种子 URL
TorrentHash     NVARCHAR NOT NULL      -- 种子哈希 (UNIQUE)
Title           NVARCHAR NOT NULL      -- 资源标题
FileSize        BIGINT NULL            -- 文件大小
Status          INT DEFAULT 0          -- 状态枚举
ErrorMessage    NVARCHAR NULL          -- 错误信息
PublishedAt     DATETIME NOT NULL      -- RSS 发布时间
DiscoveredAt    DATETIME NOT NULL      -- 发现时间
DownloadedAt    DATETIME NULL          -- 推送到 qB 时间
```

#### API 端点

| Method | Endpoint | 说明 |
|--------|----------|------|
| GET | `/api/subscription` | 获取所有订阅 |
| GET | `/api/subscription/{id}` | 获取单个订阅 |
| POST | `/api/subscription` | 创建订阅 |
| PUT | `/api/subscription/{id}` | 更新订阅 |
| DELETE | `/api/subscription/{id}` | 删除订阅 |
| POST | `/api/subscription/{id}/toggle?enabled=true` | 启用/禁用 |
| POST | `/api/subscription/{id}/check` | 手动检查更新 |
| POST | `/api/subscription/check-all` | 检查所有订阅 |
| GET | `/api/subscription/{id}/history` | 获取下载历史 |

#### 配置项 (appsettings.json)

```json
{
  "Mikan": {
    "BaseUrl": "https://mikanani.me",
    "PollingIntervalMinutes": 30,
    "EnablePolling": true,
    "MaxSubscriptionsPerPoll": 50,
    "TimeoutSeconds": 30,
    "StartupDelaySeconds": 30
  },
  "QBittorrent": {
    "Host": "localhost",
    "Port": 8080,
    "Username": "admin",
    "Password": "",
    "DefaultSavePath": null,
    "Category": "anime",
    "PauseTorrentAfterAdd": false,
    "TimeoutSeconds": 30
  }
}
```

#### 后台轮询流程

```
RssPollingService (BackgroundService)
    |
    +-- 启动后等待 StartupDelaySeconds (默认 30 秒)
    |
    +-- 循环 (每 PollingIntervalMinutes 分钟)
        |
        +-- 检查 EnablePolling 配置
        |
        +-- 对每个启用的订阅:
            |
            +-- 调用 MikanClient 获取 RSS
            +-- 解析 XML，提取资源列表
            +-- 过滤:
            |   +-- 检查 TorrentHash 是否已下载
            |   +-- 匹配字幕组筛选
            |   +-- 关键词过滤/排除
            |
            +-- 推送新资源到 qBittorrent
            +-- 记录下载历史
            +-- 更新订阅状态
```

#### 使用示例

**创建订阅**
```bash
curl -X POST http://localhost:5072/api/subscription \
  -H "Content-Type: application/json" \
  -d '{
    "bangumiId": 123456,
    "title": "我的番剧",
    "mikanBangumiId": "3141",
    "subgroupId": "583",
    "subgroupName": "ANi",
    "keywordInclude": "1080p,简体",
    "keywordExclude": "HEVC"
  }'
```

**手动检查更新**
```bash
curl -X POST http://localhost:5072/api/subscription/1/check
```

#### 相关文件

- `backend/Data/Entities/SubscriptionEntity.cs`
- `backend/Data/Entities/DownloadHistoryEntity.cs`
- `backend/Data/AnimeDbContext.cs`
- `backend/Services/Repositories/ISubscriptionRepository.cs`
- `backend/Services/Repositories/SubscriptionRepository.cs`
- `backend/Models/Configuration/MikanConfiguration.cs`
- `backend/Models/Configuration/QBittorrentConfiguration.cs`
- `backend/Models/Mikan/MikanRssModels.cs`
- `backend/Services/Interfaces/IMikanClient.cs`
- `backend/Services/Implementations/MikanClient.cs`
- `backend/Services/Interfaces/IQBittorrentService.cs`
- `backend/Services/Implementations/QBittorrentService.cs`
- `backend/Models/Dtos/SubscriptionDtos.cs`
- `backend/Services/Interfaces/ISubscriptionService.cs`
- `backend/Services/Implementations/SubscriptionService.cs`
- `backend/Controllers/SubscriptionController.cs`
- `backend/Services/Background/RssPollingService.cs`
- `backend/appsettings.json`
- `backend/Program.cs`

---

## 2026-02-03

### Backend 运行时 Bug 修复

#### 改动概述

修复了 backend 重构后的多个运行时问题，确保服务能正常启动并正确返回 TMDB 横向背景图片。

#### 修复列表

| 问题 | 文件 | 修复方案 |
|------|------|----------|
| DI 生命周期冲突 | `Program.cs` | `AnimeCacheService` 从 Singleton 改为 Scoped |
| 响应开始后修改 Headers | `ExceptionHandlerMiddleware.cs` | 添加 `HasStarted` 检查 |
| 响应开始后添加 Header | `PerformanceMonitoringMiddleware.cs` | 添加 `HasStarted` 检查 |
| `/health` 路由重复定义 | `Program.cs` | 移除 minimal API 中的重复端点 |
| HttpClient BaseAddress URL 解析错误 | `ApiClientBase.cs` | 确保 BaseAddress 以 `/` 结尾 |
| 相对 URL 路径被覆盖 | `TMDBClient.cs`, `BangumiClient.cs` | 移除 URL 开头的 `/` |
| 数据库外键约束失败 | `AnimeDbContext.cs`, Entities | 移除 AnimeImages 到 AnimeInfo 的外键约束 |

#### 问题详解

##### 1. DI 生命周期冲突

**问题**: `AnimeCacheService` 注册为 Singleton，但依赖了 Scoped 的 `IAnimeRepository`，导致启动失败。

**错误信息**:
```
Cannot consume scoped service 'IAnimeRepository' from singleton 'IAnimeCacheService'
```

**修复**:
```csharp
// 修改前
builder.Services.AddSingleton<IAnimeCacheService, AnimeCacheService>();

// 修改后
builder.Services.AddScoped<IAnimeCacheService, AnimeCacheService>();
```

##### 2. 响应开始后修改 Headers

**问题**: 中间件在响应已经开始发送后尝试修改 headers，导致异常。

**错误信息**:
```
System.InvalidOperationException: Headers are read-only, response has already started.
```

**修复**: 在修改 headers 前检查 `context.Response.HasStarted`

```csharp
// ExceptionHandlerMiddleware.cs
if (context.Response.HasStarted)
{
    _logger.LogWarning("Response has already started, cannot write error response");
    return;
}

// PerformanceMonitoringMiddleware.cs
if (!context.Response.HasStarted)
{
    context.Response.Headers.TryAdd("X-Response-Time-Ms", elapsedMs.ToString());
}
```

##### 3. HttpClient BaseAddress URL 解析问题

**问题**: .NET HttpClient 在处理相对 URL 时有特殊行为：
- BaseAddress: `https://api.themoviedb.org/3` (无尾部斜杠)
- 相对 URL: `/search/tv` (以斜杠开头)
- 结果: `https://api.themoviedb.org/search/tv` (丢失 `/3`)

**原因**: 以 `/` 开头的相对 URL 被视为绝对路径，会覆盖 BaseAddress 的路径部分。

**修复**:
```csharp
// ApiClientBase.cs - 确保 BaseAddress 以斜杠结尾
if (!baseUrl.EndsWith('/'))
    baseUrl += '/';
HttpClient.BaseAddress = new Uri(baseUrl);

// TMDBClient.cs - 相对 URL 不以斜杠开头
var url = $"search/tv?query={Uri.EscapeDataString(title)}&language={language}";
```

##### 4. 数据库外键约束

**问题**: `AnimeImagesEntity` 定义了到 `AnimeInfoEntity` 的外键，但代码尝试单独插入 Images 时，对应的 AnimeInfo 记录不存在。

**错误信息**:
```
SQLite Error 19: 'FOREIGN KEY constraint failed'
```

**修复**: 移除外键约束，让两个表独立存储

```csharp
// AnimeDbContext.cs - 移除外键配置
modelBuilder.Entity<AnimeImagesEntity>(entity =>
{
    entity.HasKey(e => e.BangumiId);
    // 移除 HasOne...WithOne...HasForeignKey 配置
});
```

#### 后端架构总结

```
backend/
├── Controllers/
│   ├── AnimeController.cs      # GET /api/anime/today
│   ├── SettingsController.cs   # GET/PUT/DELETE /api/settings/tokens
│   └── HealthController.cs     # GET /health, /health/dependencies
├── Services/
│   ├── ApiClientBase.cs        # HTTP 客户端基类（统一错误处理、日志）
│   ├── Implementations/
│   │   ├── BangumiClient.cs    # Bangumi API 客户端
│   │   ├── TMDBClient.cs       # TMDB API 客户端
│   │   ├── AniListClient.cs    # AniList GraphQL 客户端
│   │   └── AnimeAggregationService.cs  # 数据聚合服务
│   ├── TokenStorageService.cs  # Token 加密存储
│   ├── AnimeCacheService.cs    # 两级缓存（Memory + SQLite）
│   └── ResilienceService.cs    # Polly 重试策略
├── Middleware/
│   ├── ExceptionHandlerMiddleware.cs   # 全局异常处理
│   ├── CorrelationIdMiddleware.cs      # 请求追踪
│   ├── PerformanceMonitoringMiddleware.cs  # 性能监控
│   └── RequestResponseLoggingMiddleware.cs # 请求日志
├── Data/
│   ├── AnimeDbContext.cs       # EF Core SQLite 上下文
│   └── Entities/               # 数据库实体
└── Models/
    ├── Configuration/          # 配置类
    └── Dtos/                   # 数据传输对象
```

#### Token 管理机制

**优先级**: 后端配置存储 > HTTP Headers (fallback)

```csharp
// AnimeController.cs
var bangumiToken = await _tokenStorage.GetBangumiTokenAsync()
    ?? Request.Headers["X-Bangumi-Token"].FirstOrDefault();
```

**存储方式**:
- 文件: `appsettings.user.json`
- 加密: ASP.NET Core Data Protection API
- 密钥: `backend/.keys/`

**API 端点**:
- `GET /api/settings/tokens` - 查看配置状态
- `PUT /api/settings/tokens` - 保存 tokens
- `DELETE /api/settings/tokens` - 删除 tokens

#### 相关文件

- `backend/Program.cs`
- `backend/Middleware/ExceptionHandlerMiddleware.cs`
- `backend/Middleware/PerformanceMonitoringMiddleware.cs`
- `backend/Services/ApiClientBase.cs`
- `backend/Services/Implementations/TMDBClient.cs`
- `backend/Services/Implementations/BangumiClient.cs`
- `backend/Data/AnimeDbContext.cs`
- `backend/Data/Entities/AnimeImagesEntity.cs`
- `backend/Data/Entities/AnimeInfoEntity.cs`
- `backend/Services/Repositories/AnimeRepository.cs`

---

## 2026-02-02

### AnimeDetailModal 优化重构

#### 改动概述

1. **Modal 模糊效果优化** - 只模糊内容区，Sidebar 保持清晰
2. **全局语言状态集成** - Modal 根据全局语言设置显示对应语言的标题和描述
3. **布局重构** - 改为上下结构（信息区 + 下载源区）
4. **链接按钮样式优化** - 去除彩色背景，改为透明边框样式
5. **关闭按钮样式优化** - 纯 X 图标，无背景无边框

#### Junior Developer 学习要点

##### 1. Zustand 状态管理 - 跨组件通信

**场景**: Modal 打开时需要通知 HomePage 对内容区应用模糊效果

**方案**: 在 Zustand store 中添加共享状态

```tsx
// useAppStores.tsx - 添加 Modal 状态
interface AppState {
  isModalOpen: boolean;
  setModalOpen: (open: boolean) => void;
}

// AnimeInfoFlow.tsx - 打开 Modal 时设置状态
const setGlobalModalOpen = useAppStore((state) => state.setModalOpen);
const handleAnimeSelect = (anime: AnimeInfo) => {
  setSelectedAnime(anime);
  setGlobalModalOpen(true);  // 通知全局
};

// HomePage.tsx - 消费状态，条件渲染模糊
const isModalOpen = useAppStore((state) => state.isModalOpen);
<main className={`... ${isModalOpen ? 'blur-md pointer-events-none' : ''}`}>
```

**要点**: 使用 selector `(state) => state.xxx` 避免不必要的重渲染

##### 2. React Portal - Modal 渲染到 body

**场景**: Modal 需要脱离组件层级，避免被父元素的 `overflow: hidden` 或 `z-index` 影响

```tsx
import { createPortal } from "react-dom";

// 将 Modal 内容渲染到 document.body
return createPortal(modalContent, document.body);
```

**要点**: Portal 改变 DOM 位置但保留 React 事件冒泡

##### 3. 覆盖全局 CSS - all: unset

**场景**: 全局 CSS 给 `button` 设置了默认样式，需要完全重置

```css
/* index.css 中的全局按钮样式 */
button {
  background-color: #1a1a1a;
  border: 1px solid transparent;
}
button:hover {
  border-color: #646cff;
}
```

```tsx
// 使用 all: unset 完全重置
<button
  style={{
    all: 'unset',  // 重置所有继承和默认样式
    cursor: 'pointer',
    position: 'absolute',
    // ... 其他需要的样式
  }}
>
```

**要点**: `all: unset` 比逐个覆盖属性更彻底，适合需要完全自定义的组件

##### 4. Tailwind group-hover - 父子联动效果

**场景**: hover 按钮时改变内部 SVG 图标颜色

```tsx
<button className="group">
  <svg>
    <path className="stroke-gray-600 group-hover:stroke-black transition-colors" />
  </svg>
</button>
```

**要点**: `group` 标记父元素，`group-hover:` 在子元素上响应父元素的 hover

##### 5. 语言切换的 fallback 模式

**场景**: 根据语言显示标题，但某些数据可能缺失

```tsx
const language = useAppStore((state) => state.language);

// 带 fallback 的语言选择
const primaryTitle = language === 'zh'
  ? (anime.ch_title || anime.en_title)   // 中文优先，fallback 英文
  : (anime.en_title || anime.ch_title);  // 英文优先，fallback 中文

const description = language === 'zh'
  ? (anime.ch_desc || anime.en_desc || '暂无描述')
  : (anime.en_desc || anime.ch_desc || 'No description available');
```

**要点**: 使用 `||` 链式 fallback，确保始终有内容显示

#### 问题与解决方案

| 问题 | 解决方案 |
|------|----------|
| Modal 模糊整个页面包括 Sidebar | 使用 Zustand 共享状态，只对内容区 `<main>` 应用 blur |
| Modal 被父元素样式影响 | 使用 `createPortal` 渲染到 `document.body` |
| 全局 button 样式干扰关闭按钮 | 使用 `all: unset` 完全重置样式 |
| hover 效果在按钮方块上而非图标上 | 移除背景，使用 `group-hover` 只改变图标颜色 |
| 多语言内容可能缺失 | 使用 `||` 链式 fallback |

#### 相关文件

- `frontend/src/stores/useAppStores.tsx` - 新增 `isModalOpen` 状态
- `frontend/src/components/homepage/HomePage.tsx` - 条件模糊
- `frontend/src/components/homepage/content/AnimeDetailModal.tsx` - 重构布局和样式
- `frontend/src/components/homepage/content/AnimeInfoFlow.tsx` - 调用 `setModalOpen`

---

### Sidebar 按钮与标题垂直对齐修复

#### 改动点

1. **为 `.sidebar-header` 添加 `items-center`**

#### 原因

**为什么要这样改：**

- 上一次重构移除了 `items-center`（因为它与 `h-[44px]` 冲突）
- 但移除后导致 toggle button 图标和 "Anime-Sub" 文字的水平中轴线不对齐

**这样改的好处：**

- 现在没有 `h-[44px]` 的干扰，`items-center` 可以正常工作
- 按钮图标和文字在垂直方向上居中对齐，视觉一致

#### 问题与解决方案

| 问题 | 解决方案 |
|------|----------|
| toggle button 和 "Anime-Sub" 垂直不对齐 | 添加 `items-center` 到 `.sidebar-header` |

#### 示例代码

**index.css - 之前：**

```css
.sidebar-header {
  @apply w-full px-4 pt-16 pb-4 flex flex-row gap-3 mb-6;
}
```

**index.css - 之后：**

```css
.sidebar-header {
  @apply w-full px-4 pt-16 pb-4 flex flex-row items-center gap-3 mb-6;
}
```

#### 相关文件

- `frontend/src/index.css`

---

### 前端间距样式重构

#### 改动点

1. **合并 `index.css` 中重复的 `.sidebar-header` 定义**
2. **移除 `SideBar.tsx` 中的冗余内联样式 `!pt-15`**
3. **新增统一的 `.content-header` CSS 类**
4. **`HomePageContent.tsx` 使用 CSS 类替代内联 padding**

#### 原因

**为什么要这样改：**

- 原代码中 `.sidebar-header` 在 `index.css` 中定义了两次（第 126-128 行和第 130-132 行），后者覆盖前者，造成维护困难
- `h-[44px]` + `items-center` 与 `!pt-15` 同时存在时产生样式冲突，导致标题实际位置难以预测
- 间距值 `pt-15` 分散在组件内联样式中，违反 DRY 原则
- SideBar 的 "Anime-Sub" 和主内容的 "今日放送" 标题无法对齐

**这样改的好处：**

- 单一数据源：间距值统一定义在 CSS 类中，修改一处即可全局生效
- 可预测性：移除 `h-[44px]` 和 `items-center` 的干扰，padding 直接控制位置
- 可维护性：CSS 类语义化命名 `.content-header`，代码意图清晰
- 一致性：两个标题都使用 `pt-16` (64px)，确保视觉对齐

#### 问题与解决方案

| 问题 | 解决方案 |
|------|----------|
| `.sidebar-header` 重复定义 | 合并为单一定义 |
| `h-[44px]` + `items-center` 与 `pt-15` 冲突 | 移除 height 和 items-center，使用纯 padding 控制 |
| 间距值分散在内联样式 `!pt-15` | 统一定义 `.content-header` CSS 类 |
| "Anime-Sub" 与 "今日放送" 标题不对齐 | 两者都使用 `pt-16` (64px) |

#### 示例代码

**index.css - 之前：**

```css
/* 重复定义，造成混乱 */
.sidebar-header {
  @apply w-full px-2 py-3 flex items-center justify-start;
}

.sidebar-header {
  @apply w-full h-[44px] px-4 flex items-center gap-2 mb-10;
}
```

**index.css - 之后：**

```css
/* 顶部标题区域 - 统一使用 pt-16 (64px) 作为页面顶部间距 */
.sidebar-header {
  @apply w-full px-4 pt-16 pb-4 flex flex-row gap-3 mb-6;
}

/* 主内容区顶部间距 - 与 sidebar-header 对齐 */
.content-header {
  @apply pt-16;
}
```

**SideBar.tsx - 之前：**

```tsx
<div className="sidebar-header flex flex-row gap-3 !justify-start !pt-15">
```

**SideBar.tsx - 之后：**

```tsx
<div className="sidebar-header">
```

**HomePageContent.tsx - 之前：**

```tsx
<div className="w-fit overflow-hidden pt-15">
```

**HomePageContent.tsx - 之后：**

```tsx
<div className="w-fit overflow-hidden content-header">
```

#### 相关文件

- `frontend/src/index.css`
- `frontend/src/components/homepage/SideBar.tsx`
- `frontend/src/components/homepage/content/HomePageContent.tsx`

#### 参考

- [Vercel React Best Practices](https://github.com/vercel-labs/agent-skills)
- 规则：避免重复定义、单一数据源、语义化 CSS 类名
