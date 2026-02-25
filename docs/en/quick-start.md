# Quick Start

This guide deploys AnimeSub using Docker Compose — the easiest way to get started.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and [Docker Compose](https://docs.docker.com/compose/install/) installed
- qBittorrent installed with **WebUI enabled** (Tools → Options → Web UI)

## 1. Create docker-compose.yml

In your desired directory (e.g., `~/animesub/`), create a `docker-compose.yml` file:

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

> **Note**: The `./config` directory stores the database and runtime configuration. Data persists across restarts.

## 2. Start the Service

```bash
docker compose up -d
```

Once started, open your browser and navigate to:

```
http://localhost:3000
```

## 3. Complete the Setup Wizard

On first access, you will be automatically redirected to the setup wizard. There are 5 steps:

### Step 1: Create Account

Set the administrator username and password. This account is used to log into the AnimeSub web interface.

### Step 2: Configure qBittorrent

Enter your qBittorrent WebUI connection details:

| Field | Description | Example |
|-------|-------------|---------|
| Host | IP or hostname of the qBittorrent machine | `localhost` or `192.168.1.100` |
| Port | WebUI port (default: 8080) | `8080` |
| Username | WebUI login username | `admin` |
| Password | WebUI login password | `yourpassword` |

Click "Test Connection" to verify the connection before proceeding.

### Step 3: TMDB Token (Optional)

TMDB (The Movie Database) provides English metadata and landscape backdrop images.

1. Visit [TMDB Developer Settings](https://www.themoviedb.org/settings/api) to register and apply for an API token
2. Paste the **API Read Access Token** (Bearer Token) here

> If left empty, TMDB features will be disabled. Anime will still display, but without English content or landscape images.

### Step 4: Preferences

- **Display Language**: Choose Chinese or English
- **Download Path**: Set the qBittorrent save path (optional)

### Step 5: Verification

The system automatically tests all connections and displays a configuration summary. Click "Finish" to enter the main interface.

## Custom Port

If port 3000 is in use, change the `ports` section in `docker-compose.yml`:

```yaml
ports:
  - "8888:80"   # Change 3000 to your desired port
```

Then restart:

```bash
docker compose up -d
```

## Local Development (without Docker)

To run in a local development environment:

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
