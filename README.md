# AnimeSub

A self-hosted anime tracking and auto-download manager. Browse seasonal anime, subscribe to shows, and have new episodes automatically downloaded to your qBittorrent instance.

**[📖 Documentation](https://hhlyyng.github.io/AnimeSub/)**

## Features

- Browse today's airing anime with metadata from Bangumi, TMDB, and AniList
- View top anime rankings from Bangumi, AniList, and MyAnimeList
- Search anime and find available torrents via Mikan
- Subscribe to anime series for automatic episode downloads
- RSS polling (Mikan) with configurable interval, subgroup filtering, and resolution preferences
- Download history and deduplication per subscription
- Web-based settings — no config file editing required after initial deploy
- JWT authentication with a guided first-run setup wizard

## Deploy

Download `docker-compose.yml` from the [latest release](https://github.com/hhlyyng/AnimeSub/releases/latest) and run:

```bash
docker compose up -d
```

Open `http://your-server:5072` in your browser and complete the setup wizard.

```yaml
services:
  animesub:
    image: ghcr.io/hhlyyng/animesub:latest
    restart: unless-stopped
    ports:
      - "5072:5072"
    volumes:
      - ./config:/app/data    # required: maps host directory to /app/data inside the container
```

> **Note:** The volume mount is required. All data (database, config, uploads) is stored in `/app/data` inside the container. Without this mount, all data is lost when the container is removed or updated.

### Change the port

Edit the `ports` field before starting:

```yaml
ports:
  - "8080:5072"   # change 8080 to any port you prefer
```

### Persistent data

All data (database, runtime config, uploads) is stored in `./config` on the host. Back up this directory to preserve subscriptions and settings across updates.

## First-run Setup

The setup wizard guides you through:

1. Creating an admin account
2. Connecting to qBittorrent (host, port, credentials)
3. Entering your TMDB API token (optional — enables English metadata and backdrop images)
4. Setting download preferences (subgroup, resolution, subtitle type)

All settings can be changed at any time from the settings page after login.

## Development

**Backend (.NET 9)**

```bash
cd backend
dotnet restore
dotnet run
# API runs at http://localhost:5072
```

**Frontend (React + Vite)**

```bash
cd frontend
npm install
npm run dev
# Access at http://localhost:5173
```

## License

MIT
