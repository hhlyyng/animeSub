# RSS polling audit

## 审计范围与目标
- 范围 1: RSS 订阅轮询链路（`RssPollingService` -> `SubscriptionService` -> `MikanClient` -> DB/qBittorrent）
- 范围 2: 下载进度轮询链路（`DownloadProgressSyncService` -> `QBittorrentService` -> `DownloadHistory`）
- 目标: 确认“订阅特定源是否只查询固定源”“DB 是否记录订阅动漫信息”，并识别缺陷与性能优化空间。

## 一、当前实现路线

### 1. RSS 订阅轮询路线
1. `RssPollingService` 启动后按配置周期执行轮询。  
2. 调用 `ISubscriptionService.CheckAllSubscriptionsAsync()`。  
3. 从 `Subscriptions` 取启用订阅（`IsEnabled=true` 且 `BangumiId>0`）。  
4. 仅处理 `Take(MaxSubscriptionsPerPoll)` 的前 N 条订阅。  
5. 对每个订阅调用 `CheckSubscriptionInternalAsync`：
   - 用 `MikanBangumiId + SubgroupId` 请求 RSS。
   - 按 `KeywordInclude/KeywordExclude` 二次过滤。
   - 逐条检查 hash 是否已存在。
   - 新任务写入 `DownloadHistory`，再推送 qBittorrent。
   - 更新 `Subscription.LastCheckedAt / LastDownloadAt / DownloadCount`。

### 2. 下载进度轮询路线
1. `DownloadProgressSyncService` 每 30 秒执行一次。  
2. 调用 qB 接口拉取 torrent 列表（全量）。  
3. 对 qB 每个 torrent：按 hash 去 `DownloadHistory` 查记录。  
4. 命中时更新 `Progress/Speed/Eta/Seeds/Status/LastSyncedAt`。  
5. 本轮有变更则 `SaveChanges()`。

## 二、DB 是否记录当前订阅动漫信息

### 结论
`有`，且记录较完整。

### 订阅主表（Subscriptions）记录字段
- `BangumiId`
- `Title`
- `MikanBangumiId`
- `SubgroupId` / `SubgroupName`
- `KeywordInclude` / `KeywordExclude`
- `IsEnabled`
- `LastCheckedAt` / `LastDownloadAt` / `DownloadCount`

### 下载历史（DownloadHistory）订阅上下文字段
- `SubscriptionId`
- `AnimeBangumiId`
- `AnimeMikanBangumiId`
- `AnimeTitle`
- `Source=Subscription/Manual`

## 三、关键问题与风险

### 问题 A（高）: RSS 轮询存在“饥饿”风险
- 现象: 启用订阅数 > `MaxSubscriptionsPerPoll` 时，后部订阅可能长期不被轮询。
- 根因: 当前实现固定排序后直接 `Take(N)`，无轮转游标。
- 影响: 一部分订阅会持续漏检新番更新。

### 问题 B（中）: “只查询固定源”是有条件成立，不是绝对保证
- 现象: 用户选择特定字幕组时，如果 `SubgroupId` 为空，轮询会退化为“全量源 + 关键词过滤”。
- 根因: RSS URL 只有在 `SubgroupId` 非空时才带 `subgroupid` 参数。
- 影响: 可能拉到非目标源，行为与“固定源订阅”感知不一致。

### 问题 C（中）: RSS 轮询存在 N+1 查询
- 现象: 对每个候选条目都单独执行一次 `ExistsDownloadByHashAsync`。
- 根因: 未批量加载已存在 hash 集。
- 影响: 订阅多/条目多时 DB 压力线性放大。

### 问题 D（高）: 下载进度同步可能因 hash 大小写不一致而漏同步
- 现象: qB 返回 hash 与 DB hash 大小写不同，`FirstOrDefault(d => d.TorrentHash == qbTorrent.Hash)` 命中失败。
- 根因: 进度同步路径未统一 hash 规范（大小写标准化）。
- 影响: 任务实际在下载，但 DB 进度长期不更新，UI 显示滞后或错误。

### 问题 E（中）: 下载进度轮询性能开销偏高
- 现象: 每 30 秒全量拉 qB torrent + 对每个 torrent 单条查 DB。
- 根因: 全量同步 + N+1 查询。
- 影响: torrent 数量上来后 CPU/IO 与数据库压力明显增加。

### 问题 F（低）: 订阅表缺少 BangumiId 唯一约束
- 现象: 并发 ensure 可能写出重复订阅。
- 根因: 模型层仅普通索引，非唯一索引。
- 影响: 维护和轮询语义复杂化，潜在重复任务。

## 四、输入-输出示例（与预期不符）

### 示例 1: 轮询饥饿（RSS）
- 输入:
  - 启用订阅总数 = 120
  - `MaxSubscriptionsPerPoll = 50`
  - 排序固定为创建时间倒序
- 实际输出:
  - 每轮都只检查“最新 50 条”，旧 70 条可能长期不被检查。
- 预期输出:
  - 所有启用订阅应在有限轮次内都被检查到（公平轮转）。
- 如何造成不符:
  - `Take(50)` + 固定排序导致同一窗口反复命中，无游标/轮转。

### 示例 2: 固定源退化（RSS）
- 输入:
  - 用户希望订阅“字幕组 A”
  - 订阅记录里 `SubgroupId = null`，`KeywordInclude = "字幕组A,1080p"`
- 实际输出:
  - 轮询请求走 `RSS/Bangumi?bangumiId=...`（不带 `subgroupid`），先拉全量源，再关键词过滤。
- 预期输出:
  - 轮询请求应直接走 `RSS/Bangumi?bangumiId=...&subgroupid=...`，仅查询固定源。
- 如何造成不符:
  - `SubgroupId` 丢失时只能依赖关键词，失去服务端源级过滤。

### 示例 3: 进度漏同步（Download Progress）
- 输入:
  - DB 中 `TorrentHash = "ABCDEF..."`（大写）
  - qB 返回 `hash = "abcdef..."`（小写）
- 实际输出:
  - 进度同步查询未命中记录，`Progress/Status/LastSyncedAt` 不更新。
- 预期输出:
  - 应命中同一任务并更新实时进度。
- 如何造成不符:
  - 同步服务按字符串精确比较，未统一大小写规范。

## 五、结论
1. RSS 轮询主流程可工作，且 DB 对订阅上下文有完整落库。  
2. “固定源订阅”仅在 `SubgroupId` 正确落库时才严格成立。  
3. 当前主要风险在于: 轮询公平性（饥饿）、hash 规范不统一导致进度漏同步、以及两条链路的 N+1 性能问题。

## 六、建议（按优先级）
1. 高优先: 为 RSS 轮询引入公平调度（按 `LastCheckedAt` 或游标轮转），避免 `Take(N)` 饥饿。  
2. 高优先: 下载进度同步统一 hash 规范（全部 `ToUpperInvariant` 后比较）。  
3. 中优先: RSS 新项判重改为“批量查 hash + 内存集合判重”。  
4. 中优先: 下载进度同步改为“批量加载 DB 记录 + 字典匹配”，并考虑按 category 过滤 qB 列表。  
5. 低优先: 给 `Subscriptions.BangumiId` 增加唯一约束，防止并发重复订阅。
