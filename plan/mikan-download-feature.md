# Mikan RSS下载功能实现计划

## 需求背景

通过Mikan RSS源获取当前动漫所有下载磁力链接。

### 用户传递的偏好参数
1. **分辨率** - 默认1080p
2. **字幕组偏好** - 默认显示全部
3. **字幕是否内嵌** - 默认简日内嵌
4. **季度** - 默认最新季节
5. **集数** - 默认最新集数

### 功能需求
- 用户通过**下拉栏**改变参数，完成筛选功能
- 前端在Modal下载源区域可**任意下载一集**（未下载过的）
- 已订阅的动漫自动轮询，根据用户偏好参数自动下载到qBittorrent（后续实现）
- 前端提供**下载链接**和**复制磁力**两个小按钮

---

## 目标
通过Mikan RSS源获取动漫下载链接，支持用户偏好筛选和一键下载。

## 用户偏好参数（默认值）
| 参数 | 默认值 | 说明 |
|------|--------|------|
| 分辨率 | 1080p | 720p/1080p/4K |
| 字幕组 | all | 动态列表 |
| 字幕类型 | 简日内嵌 | 简日内嵌/繁日/简体/繁体 |

> 季度和集数通过RSS发布时间自动排序，最新在前

---

## 后端实现

### 1. 新增API端点

**文件**: `backend/Controllers/MikanController.cs` (新建)

```
GET /api/mikan/search?title={jp_title}
```
- 搜索Mikan获取动漫RSS列表
- 解析标题提取分辨率、字幕组、集数等元数据

```
POST /api/mikan/download
```
- 推送磁力链接到qBittorrent
- Body: `{ magnetLink, torrentUrl, title, torrentHash }`

### 2. 标题解析服务

**文件**: `backend/Services/Implementations/TorrentTitleParser.cs` (新建)

从RSS标题解析元数据：
```csharp
public class ParsedTorrentInfo
{
    public string? Resolution { get; set; }     // "1080p"
    public string? Subgroup { get; set; }       // "ANi"
    public string? SubtitleType { get; set; }   // "简日内嵌"
    public int? Episode { get; set; }           // 1
}
```

正则规则：
- 分辨率: `(1080[pP]|720[pP]|4[kK])`
- 字幕组: `^\[([^\]]+)\]` (首个方括号)
- 集数: `(?:第|EP?)(\d+)|[\s_-](\d{2,3})[\s_\[\.]`
- 字幕: `(简日|繁日|简体|繁体|内嵌|CHT|CHS)`

### 3. 扩展MikanClient

**文件**: `backend/Services/Implementations/MikanClient.cs`

新增搜索方法：
```csharp
Task<MikanSearchResult?> SearchAnimeAsync(string title);
```
- 请求: `GET https://mikanani.me/Home/Search?searchstr={title}`
- 解析HTML提取Mikan Bangumi ID

### 4. 需修改的文件

| 文件 | 修改内容 |
|------|---------|
| `backend/Controllers/MikanController.cs` | 新建 - 搜索和下载端点 |
| `backend/Services/Implementations/TorrentTitleParser.cs` | 新建 - 标题解析 |
| `backend/Services/Interfaces/ITorrentTitleParser.cs` | 新建 - 接口 |
| `backend/Services/Implementations/MikanClient.cs` | 添加SearchAnimeAsync |
| `backend/Services/Interfaces/IMikanClient.cs` | 添加接口方法 |
| `backend/Models/Dtos/MikanSearchDtos.cs` | 新建 - DTO定义 |
| `backend/Program.cs` | 注册新服务 |

---

## 前端实现

### 1. 扩展AnimeDetailModal下载区域

**文件**: `frontend/src/components/homepage/content/AnimeDetailModal.tsx`

替换第228-231行的占位符：
```tsx
<div className="border-t border-gray-200 p-6 bg-gray-50">
  <h3 className="text-lg font-bold text-gray-900 mb-4">{downloadLabel}</h3>

  {/* 筛选下拉栏 */}
  <div className="flex gap-3 mb-4">
    <select>分辨率</select>
    <select>字幕组</select>
    <select>字幕类型</select>
  </div>

  {/* 下载列表 */}
  <div className="space-y-2 max-h-60 overflow-y-auto">
    {filteredItems.map(item => (
      <DownloadItem
        item={item}
        onDownload={handleDownload}
        onCopyMagnet={handleCopyMagnet}
      />
    ))}
  </div>
</div>
```

### 2. 新增组件

**文件**: `frontend/src/components/homepage/content/DownloadItem.tsx` (新建)

每个下载项显示：
- 集数徽章 | 标题(截断) | 文件大小 | 发布时间
- 分辨率徽章 | 字幕组徽章
- [下载] [复制磁力] 两个按钮

### 3. 扩展Zustand Store

**文件**: `frontend/src/stores/useAppStores.tsx`

```tsx
interface AppState {
  // 新增
  downloadPreferences: {
    resolution: '1080p' | '720p' | '4K' | 'all';
    subgroup: string;
    subtitleType: string;
  };
  setDownloadPreferences: (prefs: Partial<DownloadPreferences>) => void;
}
```

持久化到localStorage的`anime-app-storage`键。

### 4. 新增API服务

**文件**: `frontend/src/services/mikanApi.ts` (新建)

```typescript
export async function searchMikanAnime(title: string): Promise<MikanSearchResult>
export async function downloadTorrent(item: RssItem): Promise<void>
```

### 5. 需修改的文件

| 文件 | 修改内容 |
|------|---------|
| `frontend/src/components/homepage/content/AnimeDetailModal.tsx` | 集成下载功能 |
| `frontend/src/components/homepage/content/DownloadItem.tsx` | 新建 - 下载项组件 |
| `frontend/src/stores/useAppStores.tsx` | 添加downloadPreferences |
| `frontend/src/services/mikanApi.ts` | 新建 - API调用 |
| `frontend/src/types/mikan.ts` | 新建 - TypeScript类型 |

---

## 数据流

```
用户打开AnimeDetailModal
    ↓
调用 GET /api/mikan/search?title={jp_title}
    ↓
后端 MikanClient.SearchAnimeAsync(title)
    ├── 爬取 mikanani.me/Home/Search
    └── 解析获取 MikanBangumiId
    ↓
后端 MikanClient.GetAnimeFeedAsync(mikanId)
    ↓
后端 TorrentTitleParser.Parse(items)
    ↓
返回带解析元数据的RSS列表
    ↓
前端按偏好筛选显示
    ↓
用户点击[下载] → POST /api/mikan/download → qBittorrent
用户点击[复制磁力] → navigator.clipboard.writeText()
```

---

## 实现顺序

### Phase 1: 后端基础 (3个文件)
1. `TorrentTitleParser.cs` + 接口
2. `MikanClient.cs` 添加SearchAnimeAsync
3. `MikanController.cs` 搜索和下载端点

### Phase 2: 前端组件 (4个文件)
1. `mikan.ts` 类型定义
2. `mikanApi.ts` API服务
3. `DownloadItem.tsx` 下载项组件
4. `AnimeDetailModal.tsx` 集成

### Phase 3: 状态管理 (1个文件)
1. `useAppStores.tsx` 添加偏好设置

---

## 验证方法

1. **后端测试**:
   - `GET /api/mikan/search?title=葬送的芙莉蓮` 返回RSS列表
   - `POST /api/mikan/download` 成功推送到qBittorrent

2. **前端测试**:
   - 打开任意动漫Modal，显示下载源列表
   - 下拉筛选正常过滤
   - 点击下载按钮，qBittorrent收到任务
   - 点击复制磁力，剪贴板有内容

---

## 关键复用

- `QBittorrentService.AddTorrentAsync()` - 现有下载推送
- `MikanClient.GetAnimeFeedAsync()` - 现有RSS获取
- `MikanRssItem` 模型 - 现有数据结构
- Zustand persist模式 - 现有持久化逻辑

---

## 决策记录

- **ID映射方案**: 通过搜索Mikan网站标题实现（用户确认）
- **订阅系统集成**: 暂不集成，本次只实现Modal中的手动下载功能（用户确认）
