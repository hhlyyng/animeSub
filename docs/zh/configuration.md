# 配置参考

AnimeSub 的配置通过安装向导写入 `appsettings.runtime.json`，保存在 Docker 数据卷的 `/app/data/` 目录下。你也可以直接编辑该文件（修改后需重启容器）。

## QBittorrent

qBittorrent WebUI 连接配置。

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Host` | string | `localhost` | qBittorrent 主机地址（IP 或域名） |
| `Port` | int | `8080` | WebUI 端口 |
| `Username` | string | `admin` | WebUI 登录用户名 |
| `Password` | string | `""` | WebUI 登录密码 |
| `Category` | string | `anime` | 下载任务分类标签 |
| `DefaultSavePath` | string? | `null` | 默认保存路径（留空则使用 qBittorrent 默认路径） |
| `UseAnimeSubPath` | bool | `false` | 是否按番剧名称创建子目录 |
| `PauseTorrentAfterAdd` | bool | `false` | 添加后是否暂停（手动管理时有用） |
| `TimeoutSeconds` | int | `30` | 请求超时时间（秒） |

**示例**：

```json
{
  "QBittorrent": {
    "Host": "192.168.1.100",
    "Port": 8080,
    "Username": "admin",
    "Password": "mypassword",
    "Category": "anime",
    "DefaultSavePath": "/downloads/anime",
    "PauseTorrentAfterAdd": false,
    "TimeoutSeconds": 30
  }
}
```

## Mikan

Mikan RSS 订阅源配置。

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `BaseUrl` | string | `https://mikanani.me` | Mikan 基础地址 |
| `PollingIntervalMinutes` | int | `30` | RSS 轮询间隔（分钟） |
| `EnablePolling` | bool | `true` | 是否启用后台自动轮询 |
| `MaxSubscriptionsPerPoll` | int | `50` | 每次轮询最多处理的订阅数量 |
| `TimeoutSeconds` | int | `30` | 请求超时时间（秒） |
| `StartupDelaySeconds` | int | `30` | 启动后等待多少秒再开始首次轮询 |

**示例**：

```json
{
  "Mikan": {
    "BaseUrl": "https://mikanani.me",
    "PollingIntervalMinutes": 60,
    "EnablePolling": true,
    "MaxSubscriptionsPerPoll": 50,
    "TimeoutSeconds": 30,
    "StartupDelaySeconds": 30
  }
}
```

## ApiTokens

外部 API Token 配置。

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `TmdbToken` | string | `""` | TMDB API Read Access Token（Bearer Token） |

> **Bangumi Token**：Bangumi 公开 API 无需 Token，系统会直接使用匿名访问。

**示例**：

```json
{
  "ApiTokens": {
    "TmdbToken": "eyJhbGciOiJIUzI1NiJ9..."
  }
}
```

## Auth

JWT 认证配置。

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `JwtSecret` | string | （安装向导随机生成） | JWT 签名密钥（请勿手动修改） |
| `TokenExpirationDays` | int | `7` | Token 有效期（天） |

> 安装向导会自动生成强随机密钥，通常无需手动设置。

## PreFetch

数据预取服务配置。

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Enabled` | bool | `true` | 是否启用定时预取 |
| `ScheduleHour` | int | `3` | 每天几点执行预取（0-23，24 小时制） |
| `RunOnStartup` | bool | `true` | 启动时立即运行一次预取 |
| `MaxConcurrency` | int | `3` | 并发聚合请求数 |

**示例**：

```json
{
  "PreFetch": {
    "Enabled": true,
    "ScheduleHour": 3,
    "RunOnStartup": true,
    "MaxConcurrency": 3
  }
}
```

## 完整示例

下面是一个完整的 `appsettings.runtime.json` 示例：

```json
{
  "QBittorrent": {
    "Host": "localhost",
    "Port": 8080,
    "Username": "admin",
    "Password": "yourpassword",
    "Category": "anime",
    "DefaultSavePath": null,
    "UseAnimeSubPath": false,
    "PauseTorrentAfterAdd": false,
    "TimeoutSeconds": 30
  },
  "Mikan": {
    "BaseUrl": "https://mikanani.me",
    "PollingIntervalMinutes": 30,
    "EnablePolling": true,
    "MaxSubscriptionsPerPoll": 50,
    "TimeoutSeconds": 30,
    "StartupDelaySeconds": 30
  },
  "ApiTokens": {
    "TmdbToken": "your_tmdb_bearer_token"
  },
  "Auth": {
    "JwtSecret": "auto_generated_secret",
    "TokenExpirationDays": 7
  },
  "PreFetch": {
    "Enabled": true,
    "ScheduleHour": 3,
    "RunOnStartup": true,
    "MaxConcurrency": 3
  }
}
```

## 注意事项

- 修改配置文件后，需要重启容器才能生效：`docker compose restart`
- `JwtSecret` 修改后，所有已登录用户的 Token 将失效，需要重新登录
- `appsettings.runtime.json` 优先于 `appsettings.json`，安装向导的所有修改都写入前者
