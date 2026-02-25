# 更新日志

## 2026-02-05

### 修复：Bangumi Token 可选化

经 API 测试确认，Bangumi 公开端点（每日放送、评分排行、条目详情）无需认证。

**变更**：
- Bangumi Token 改为可选，缺少时直接使用公开 API，不再返回 401/400 错误
- API 端点从 `POST /v0/search/subjects` 改为 `GET /v0/subjects?type=2&sort=rank`

### 新增：数据库 Schema

系统使用 SQLite 数据库持久化存储，主要数据表：

| 表名 | 说明 |
|------|------|
| `AnimeInfo` | 聚合动漫信息（标题、评分、图片、链接） |
| `AnimeImages` | 图片缓存（海报、背景图） |
| `Subscriptions` | 订阅配置 |
| `DownloadHistory` | 下载历史（按 TorrentHash 去重） |
| `DailyScheduleCache` | 每日放送缓存 |

---

## 2026-02-04

### 新增：Top 10 排行功能

新增三个 API 端点，展示不同信源的 Top 10 动漫：

| 端点 | 数据来源 |
|------|----------|
| `GET /api/anime/top/bangumi` | Bangumi 评分排行 |
| `GET /api/anime/top/anilist` | AniList 趋势排行 |
| `GET /api/anime/top/mal` | MyAnimeList via Jikan API |

前端主页新增 3 个横向滚动 AnimeFlow 卡片区域。

### 新增：预取架构

将后端从实时 API 聚合改为预取 + 增量更新架构：

- 响应时间从 3–10 秒优化到 **<100ms**
- 后台 `AnimePreFetchService` 每日凌晨 3 点批量预取全周数据
- 新增管理 API：`GET /api/admin/prefetch/status`、`POST /api/admin/prefetch`

### 新增：TMDB 匹配优化

- **动画优先**：优先选择 Animation 类型（genre_id=16），避免真人剧污染结果
- **日本出品优先**：同等条件下优先 origin_country=JP
- **多季度匹配**：利用 Bangumi `air_date` 年份过滤，匹配对应季度专属图片
- **中文简介备选**：Bangumi 简介为空时自动使用 TMDB 中文翻译

### 新增：Mikan RSS 订阅系统

完整的订阅与自动下载功能：

- 订阅管理 CRUD（9 个 REST API 端点）
- 后台 `RssPollingService`（每 30 分钟轮询）
- 关键词包含/排除过滤、字幕组筛选
- TorrentHash 去重，防止重复下载
- qBittorrent WebUI 集成

---

## 2026-02-03

### 修复：Backend 运行时 Bug

修复重构后的多个运行时问题：

| 问题 | 修复方案 |
|------|----------|
| DI 生命周期冲突（Singleton 依赖 Scoped） | `AnimeCacheService` 改为 Scoped |
| 响应开始后修改 Headers 导致异常 | 添加 `HasStarted` 检查 |
| HttpClient BaseAddress 路径被覆盖 | 确保 BaseAddress 以 `/` 结尾，相对 URL 不以 `/` 开头 |
| SQLite 外键约束失败 | 移除 AnimeImages → AnimeInfo 外键，两表独立存储 |

---

## 2026-02-02

### 新增：AnimeDetailModal 优化

- Modal 仅模糊主内容区，Sidebar 保持清晰（通过 Zustand 共享状态）
- 使用 `createPortal` 渲染到 `document.body`，脱离父元素样式影响
- 布局改为上下结构（信息区 + 下载源区）
- 根据全局语言设置显示对应语言的标题和简介，支持 fallback

### 修复：Sidebar 与标题对齐

为 `.sidebar-header` 添加 `items-center`，修复 toggle 按钮图标与 "Anime-Sub" 文字垂直不对齐问题。

### 重构：前端间距样式

- 合并 `index.css` 中重复的 `.sidebar-header` 定义
- 新增 `.content-header` CSS 类，统一管理主内容区顶部间距
- Sidebar 标题与主内容标题对齐（均使用 `pt-16` = 64px）
