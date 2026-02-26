# Quick Start

This guide deploys AnimeSub using Docker — the easiest way to get started.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) installed
- qBittorrent installed with **WebUI enabled** (Tools → Options → Web UI)

## 1. Create docker-compose.yml

In your desired directory (e.g., `/opt/animesub/`), create a `docker-compose.yml` file:

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

Or download it directly from the [latest release](https://github.com/hhlyyng/AnimeSub/releases/latest).

## 2. Start the Service

```bash
docker compose up -d
```

Once started, open your browser and navigate to:

```
http://localhost:5072
```

## 3. Complete the Setup Wizard

On first access, you will be automatically redirected to the setup wizard. There are 4 steps:

### Step 1: Create Account

Set the administrator username and password.

### Step 2: Configure qBittorrent

Enter your qBittorrent WebUI connection details:

| Field | Description | Example |
|-------|-------------|---------|
| Host | IP or hostname of the qBittorrent machine | `localhost` or `192.168.1.100` |
| Port | WebUI port (default: 8080) | `8080` |
| Username | WebUI login username | `admin` |
| Password | WebUI login password | `yourpassword` |

Click "Test Connection" to verify before proceeding.

### Step 3: TMDB Token (Optional)

1. Visit [TMDB Developer Settings](https://www.themoviedb.org/settings/api) to register and apply for an API token
2. Paste the **API Read Access Token** (Bearer Token) here

> If left empty, English metadata and landscape backdrop images will be disabled.

### Step 4: Preferences & Verification

Set display language and download preferences. The system automatically verifies all connections and completes setup.

## Custom Port

Edit the `ports` section in `docker-compose.yml`:

```yaml
ports:
  - "8080:5072"   # change 8080 to your desired port
```

Then restart:

```bash
docker compose up -d
```

## No Command-Line Deployment

If your environment doesn't support a command line (e.g., enterprise NAS, Portainer), download the tar image for your architecture from the [latest release](https://github.com/hhlyyng/AnimeSub/releases/latest) (`animesub-vX.X.X-arm64.tar` or `animesub-vX.X.X-amd64.tar`), import it via your management UI, and start the container with:

| Setting | Value |
|---------|-------|
| Port mapping | host port → `5072` |
| Volume mount | host directory → `/app/data` |

## Local Development

**Backend**

```bash
cd backend
dotnet restore
dotnet run
# API runs at http://localhost:5072
```

**Frontend**

```bash
cd frontend
npm install
npm run dev
# Access at http://localhost:5173
```
