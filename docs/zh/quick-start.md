# 快速开始

本指南通过 Docker Compose 部署 AnimeSub，这是最简单的部署方式。

## 前置条件

- [Docker](https://docs.docker.com/get-docker/) 和 [Docker Compose](https://docs.docker.com/compose/install/) 已安装
- qBittorrent 已安装并**启用 WebUI**（设置 → 高级 → Web 用户界面）

## 1. 创建 docker-compose.yml

在你选择的目录下（例如 `~/animesub/`），创建 `docker-compose.yml` 文件：

```yaml
services:
  animesub:
    image: ghcr.io/hhlyyng/anime-subscription:latest
    container_name: animesub
    ports:
      - "3000:80"
    volumes:
      - ./config:/app/data
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
```

> **说明**：`./config` 目录将存储数据库文件和运行时配置，重启后数据不会丢失。

## 2. 启动服务

```bash
docker compose up -d
```

启动完成后，在浏览器访问：

```
http://localhost:3000
```

## 3. 完成安装向导

首次访问时，系统会自动跳转到安装向导。共 5 个步骤：

### 步骤 1：创建账户

设置管理员用户名和密码。此账户用于登录 AnimeSub Web 界面。

### 步骤 2：配置 qBittorrent

填写 qBittorrent WebUI 连接信息：

| 字段 | 说明 | 示例 |
|------|------|------|
| 主机地址 | qBittorrent 所在主机 IP 或域名 | `localhost` 或 `192.168.1.100` |
| 端口 | WebUI 端口（默认 8080） | `8080` |
| 用户名 | WebUI 登录用户名 | `admin` |
| 密码 | WebUI 登录密码 | `yourpassword` |

填写完成后点击"测试连接"，确认连接成功再进入下一步。

### 步骤 3：TMDB Token（可选）

TMDB（The Movie Database）提供英文元数据和横版背景图。

1. 访问 [TMDB 开发者页面](https://www.themoviedb.org/settings/api) 注册并申请 API Token
2. 将 **API Read Access Token**（Bearer Token）粘贴到此处

> 如果不填写，TMDB 相关功能将被禁用，动漫仍会显示但无英文内容和横版图片。

### 步骤 4：偏好设置

- **显示语言**：选择中文或英文
- **下载路径**：设置 qBittorrent 下载保存路径（可选）

### 步骤 5：验证

系统自动测试所有连接，展示配置摘要。确认无误后点击"完成"，进入主界面。

## 自定义端口

如果 3000 端口已被占用，修改 `docker-compose.yml` 中的 `ports` 配置：

```yaml
ports:
  - "8888:80"   # 将 3000 改为你想要的端口
```

然后重启服务：

```bash
docker compose up -d
```

## 本地开发（不使用 Docker）

如需在本地开发环境运行：

**后端**

```bash
cd backend
dotnet restore
dotnet run
# API 运行在 http://localhost:5072
```

**前端**

```bash
cd frontend
npm install
npm run dev
# 访问 http://localhost:5173
```
