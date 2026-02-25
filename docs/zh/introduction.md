# 项目介绍

AnimeSub 是一个自托管的动漫订阅与自动下载应用。它聚合多个动漫数据库，展示每日放送计划，并通过 Mikan RSS 实现番剧的自动追踪与下载。

## 功能一览

- **每日放送**：按星期展示动漫放送时间表，数据来自 Bangumi，附带 TMDB 横版背景图
- **Top 10 排行**：同时展示 Bangumi、AniList 趋势、MyAnimeList 三榜排行
- **搜索下载**：以 Mikan 为主源搜索番剧资源，支持按分辨率、字幕类型筛选
- **订阅管理**：创建/编辑/删除订阅，支持关键词包含/排除、字幕组筛选
- **自动下载**：RSS 后台轮询服务，新资源自动推送到 qBittorrent
- **下载管理**：查看种子列表、暂停/恢复/删除任务、实时进度追踪
- **Web 设置**：浏览器内完成所有配置，无需编辑配置文件

## 系统架构

### 数据聚合流程

```
前端请求 → AnimeController
    ├── Bangumi API  → 每日放送、评分、竖版封面、放送日期（主数据源）
    ├── TMDB API     → 英文元数据、横版背景图（智能匹配）
    └── AniList GraphQL → 英文标题/简介（备用）
    ↓
AnimeInfo[] → 前端缓存至 sessionStorage
```

**TMDB 智能匹配策略**：
- 优先选择 Animation 类型（genre_id=16），避免真人剧污染结果
- 同等条件下优先日本出品（origin_country=JP）
- 利用 Bangumi `air_date` 年份过滤多季度系列，匹配对应季度图片

### 订阅与下载流程

```
用户创建订阅 → SubscriptionController
    ↓
RssPollingService（后台服务，每 30 分钟）
    ├── 获取 Mikan RSS 订阅源
    ├── 按字幕组/关键词过滤
    ├── 与 DownloadHistory 对比去重（按 torrent hash）
    └── 推送新种子到 qBittorrent
```

### 预取架构

为了实现 <100ms 的响应时间，系统在凌晨批量预取全周放送数据并存入 SQLite 数据库：

```
预取服务（每日凌晨 3 点）
    ↓
获取完整周历 → 批量聚合 Bangumi + TMDB + AniList 数据
    ↓
写入 SQLite → 前端请求直接读取数据库（<100ms）
```

| 对比项 | 实时聚合（旧） | 预取架构（现） |
|--------|---------------|---------------|
| 响应时间 | 3–10 秒 | <100ms |
| API 调用时机 | 每次请求 | 仅凌晨批量 |
| 故障影响 | 外部 API 故障影响用户 | 数据库保底，API 故障不感知 |

## 技术栈

| 层级 | 技术 |
|------|------|
| 前端 | React 19 + TypeScript + Vite + Tailwind CSS + Zustand |
| 后端 | .NET 9 ASP.NET Core |
| 数据库 | SQLite（通过 Entity Framework Core） |
| 容器化 | Docker + Docker Compose |
| 认证 | JWT（HMAC-SHA256）+ bcrypt |

## 外部服务依赖

| 服务 | 用途 | 是否必须 |
|------|------|----------|
| [Bangumi](https://bangumi.tv) | 每日放送、评分、封面图 | 是 |
| [TMDB](https://www.themoviedb.org) | 英文元数据、横版背景图 | 否（禁用则无英文内容和背景图） |
| [AniList](https://anilist.co) | 英文标题/简介备用 | 否 |
| [Mikan](https://mikanani.me) | 番剧 RSS 资源 | 订阅功能需要 |
| qBittorrent | 种子下载管理 | 下载功能需要 |
