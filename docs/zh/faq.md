# 常见问题

## 如何获取 TMDB API Token？

1. 访问 [TMDB 官网](https://www.themoviedb.org/) 注册账号（免费）
2. 进入 [账户设置 → API](https://www.themoviedb.org/settings/api)
3. 申请 Developer（开发者）类型的 API 密钥
4. 审批通过后，在页面找到 **API Read Access Token**（以 `eyJ` 开头的长字符串）
5. 将该 Token 填入 AnimeSub 安装向导的第 3 步，或在设置页面更新

> **注意**：需要填写的是 "API Read Access Token"（Bearer Token），**不是** 较短的 "API Key"。

## TMDB Token 是必填的吗？

不是必填项。不填写 TMDB Token 时：

- 动漫仍然正常展示（来自 Bangumi）
- 英文标题、英文简介将不可用
- 横版背景图将不可用（只显示竖版封面）
- Top 10 排行中的 AniList 和 MAL 功能不受影响

## qBittorrent 连接失败怎么办？

请按以下步骤排查：

1. **确认 WebUI 已启用**：打开 qBittorrent → 工具 → 选项 → Web 用户界面 → 勾选"启用 Web 用户界面"
2. **确认端口正确**：默认端口是 8080，如果已修改请填写实际端口
3. **确认 IP 地址**：
   - 如果 qBittorrent 和 AnimeSub 在同一台机器：填 `localhost` 或 `127.0.0.1`
   - 如果在不同机器：填 qBittorrent 所在机器的局域网 IP（如 `192.168.1.100`）
   - Docker 内访问宿主机：填 `host.docker.internal`（Windows/Mac）
4. **确认用户名密码正确**：在浏览器直接访问 `http://IP:8080` 验证
5. **检查防火墙**：确认 8080 端口没有被防火墙拦截

## 如何修改应用端口？

编辑 `docker-compose.yml` 中的 `ports` 配置：

```yaml
ports:
  - "8888:80"   # 左边的 8888 是你访问时用的端口
```

修改后重新启动：

```bash
docker compose up -d
```

## 数据存储在哪里？

所有持久化数据存储在 Docker 数据卷映射的 `./config` 目录下：

| 文件 | 内容 |
|------|------|
| `./config/anime.db` | SQLite 数据库（动漫数据、订阅、下载历史） |
| `./config/appsettings.runtime.json` | 运行时配置（qBittorrent、API Token、Auth 等） |

## 如何重置账户密码？

删除运行时配置文件并重启，系统会重新进入安装向导：

```bash
# 停止容器
docker compose down

# 删除运行时配置（只删除配置，数据库保留）
rm ./config/appsettings.runtime.json

# 重新启动
docker compose up -d
```

访问 `http://localhost:3000`，系统会跳转到安装向导重新配置。

> **注意**：这不会删除数据库（订阅和下载历史）。如需完全重置，删除整个 `./config` 目录。

## 订阅未自动下载怎么排查？

按以下顺序检查：

1. **确认订阅状态为"已启用"**（订阅列表页面查看）
2. **确认 RSS 轮询已开启**：设置页面检查 `EnablePolling: true`
3. **手动触发一次检查**：点击订阅旁边的"检查"按钮，查看是否有新资源
4. **确认 qBittorrent 连接正常**：设置页面点击"测试连接"
5. **检查关键词过滤是否过于严格**：尝试清空包含/排除关键词后再手动检查
6. **查看日志**：`docker compose logs animesub` 查看详细错误信息

## 如何查看应用日志？

```bash
# 查看实时日志
docker compose logs -f animesub

# 查看最近 100 行日志
docker compose logs --tail=100 animesub
```

## 可以运行多个实例吗？

不建议。多个实例共享同一个 SQLite 数据库文件时可能发生写冲突。如果需要高可用，请考虑将数据库迁移到 PostgreSQL（需修改代码）。

## 更新到新版本

```bash
# 拉取最新镜像
docker compose pull

# 重启服务（数据不会丢失）
docker compose up -d
```
