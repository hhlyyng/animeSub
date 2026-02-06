# 测试说明 - 排查下载源不显示问题

## 步骤

### 1. 启动后端
```bash
cd backend
dotnet run
```

后端将监听 `http://localhost:5072`

### 2. 启动前端
```bash
cd frontend
npm run dev
```

前端将运行在 `http://localhost:5173`

### 3. 测试API端点

#### 测试搜索API
打开浏览器或使用curl：
```bash
curl "http://localhost:5072/api/mikan/search?title=葬送的芙莉蓮"
```

期望结果：返回JSON包含seasons数组，例如：
```json
{
  "animeTitle": "葬送的芙莉蓮",
  "seasons": [
    {
      "seasonName": "Season 1",
      "mikanBangumiId": "xxx",
      "year": 2023
    }
  ],
  "defaultSeason": 0
}
```

#### 测试RSS源API
使用第一步返回的mikanBangumiId：
```bash
curl "http://localhost:5072/api/mikan/feed?mikanId=YOUR_MIKAN_ID"
```

期望结果：返回RSS条目数组。

### 4. 前端测试

1. 打开浏览器到 `http://localhost:5173`
2. 按F12打开开发者工具，切换到Console标签
3. 点击任意动漫卡片打开Modal
4. 观察控制台输出

#### 期望的日志：
- `Modal opened, anime: {...}`
- `Loading seasons for anime: 葬送的芙莉蓮`
- `Search result: {...}`
- 或错误信息

### 5. 查看后端日志

在后端终端窗口查看日志输出：
- `Mikan search endpoint called with title: 葬送的芙莉蓮`
- `Searching Mikan for: 葬送的芙莉蓮, URL: Home/Search?searchstr=...`
- `Mikan search HTML content length: XXXX chars`
- `Search result found: AnimeTitle=..., SeasonsCount=X, DefaultSeason=0`
- 或警告/错误信息

### 6. 常见问题排查

#### 问题1: 搜索返回404
**原因**: 网络问题或Mikan网站不可达
**解决**: 检查网络连接，确认可以访问 https://mikanani.me

#### 问题2: 搜索返回"未找到结果"
**原因**: HTML解析失败或选择器不匹配
**解决**: 查看后端日志中的HTML内容长度

#### 问题3: 前端显示"暂无可用下载源"
**原因**: search API返回null或seasons为空
**解决**: 检查浏览器控制台的错误信息

#### 问题4: 前端加载状态一直显示
**原因**: API请求挂起或失败
**解决**: 检查网络请求（Network标签）查看请求状态码

## 调试建议

1. **先测试API端点**: 使用curl直接测试后端API，确保返回正确数据
2. **检查浏览器Network**: 看API请求是否成功、返回什么数据
3. **查看后端日志**: 详细的日志会告诉你解析到哪里失败了
4. **检查前端Console**: JavaScript错误会影响功能运行

## 预期行为

- ✅ 打开Modal后，显示加载动画
- ✅ 加载完成后，显示"下载源"区域
- ✅ 如果找到数据，显示季度选择器（如果有多个季度）
- ✅ 显示筛选下拉框（分辨率、字幕组、字幕）
- ✅ 显示下载列表（每个条目包含集数、标题、按钮）
- ✅ 如果没找到数据，显示"暂无可用下载源"错误消息
