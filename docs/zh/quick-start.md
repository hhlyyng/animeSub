# 快速开始

本指南通过 Docker 部署 AnimeSub，这是最简单的部署方式。

## 前置条件

- [Docker](https://docs.docker.com/get-docker/) 已安装
- qBittorrent 已安装并**启用 WebUI**（设置 → 高级 → Web 用户界面）

## 1. 创建 docker-compose.yml

在你选择的目录下（例如 `/opt/animesub/`），创建 `docker-compose.yml` 文件：

```yaml
services:
  animesub:
    image: ghcr.io/hhlyyng/animesub:latest
    restart: unless-stopped
    ports:
      - "5072:5072"
    volumes:
      - ./config:/app/data
```

或者直接从 [Release 页面](https://github.com/hhlyyng/AnimeSub/releases/latest) 下载 `docker-compose.yml`。

## 2. 启动服务

```bash
docker compose up -d
```

启动完成后，在浏览器访问：

```
http://localhost:5072
```

## 3. 完成安装向导

首次访问时，系统会自动跳转到安装向导。共 4 个步骤：

### 步骤 1：创建账户

设置管理员用户名和密码。

### 步骤 2：配置 qBittorrent

填写 qBittorrent WebUI 连接信息：

| 字段 | 说明 | 示例 |
|------|------|------|
| 主机地址 | qBittorrent 所在主机 IP | `localhost` 或 `192.168.1.100` |
| 端口 | WebUI 端口（默认 8080） | `8080` |
| 用户名 | WebUI 登录用户名 | `admin` |
| 密码 | WebUI 登录密码 | `yourpassword` |

### 步骤 3：TMDB Token（可选）

1. 访问 [TMDB 开发者页面](https://www.themoviedb.org/settings/api) 注册并申请 API Token
2. 将 **API Read Access Token**（Bearer Token）粘贴到此处

> 不填写则禁用英文元数据和横版背景图功能。

### 步骤 4：偏好设置与验证

设置显示语言和下载偏好，系统自动验证所有连接后完成安装。

## 自定义端口

修改 `docker-compose.yml` 中的 `ports`：

```yaml
ports:
  - "8080:5072"   # 左边改为你想要的端口
```

然后重启：

```bash
docker compose up -d
```

## 无命令行部署

如果你的环境不支持命令行（如企业 NAS、Portainer 等），可以从 [Release 页面](https://github.com/hhlyyng/AnimeSub/releases/latest) 下载对应架构的 tar 镜像包（`animesub-vX.X.X-arm64.tar` 或 `animesub-vX.X.X-amd64.tar`），通过管理界面导入后按如下参数启动容器：

| 项目 | 值 |
|------|-----|
| 端口映射 | 宿主机端口 → `5072` |
| 挂载路径 | 宿主机目录 → `/app/data` |

## 本地开发

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
