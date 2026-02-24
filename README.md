# Anime Subscription

A self-hosted anime tracking and auto-download manager. Browse seasonal anime, subscribe to shows, and have new episodes automatically downloaded to your qBittorrent instance.

## Features

- Browse today's airing anime with metadata from Bangumi, TMDB, and AniList
- View top anime rankings from Bangumi, AniList, and MyAnimeList
- Search anime and find available torrents via Mikan
- Subscribe to anime series for automatic episode downloads
- RSS polling (Mikan) with configurable interval, subgroup filtering, and resolution preferences
- Download history and deduplication per subscription
- Web-based settings — no config file editing required after initial deploy
- JWT authentication with a guided first-run setup wizard

## Requirements

- Docker and Docker Compose
- A running qBittorrent instance accessible from the host
- A TMDB API read token (free at https://www.themoviedb.org/settings/api)

## Deploy

Download `docker-compose.yml` from the latest release and run:

```bash
docker compose up -d
```

Open `http://your-server:3000` in your browser and complete the setup wizard.

### Change the port

Edit the `ports` field in `docker-compose.yml` before starting:

```yaml
frontend:
  ports:
    - "8888:80"   # change 8888 to any port you prefer
```

### Persistent data

All application data (database, runtime settings, encryption keys) is stored in `./config` on the host. Back up this directory to preserve subscriptions and configuration across updates.

## First-run Setup

The setup wizard will guide you through:

1. Creating an admin account
2. Entering your TMDB API token
3. Connecting to qBittorrent (host, port, credentials, save path)
4. Setting download preferences (subgroup, resolution, subtitle type)

All settings can be changed at any time from the settings page after login.

## Development

**Backend (.NET 9)**

```bash
cd backend
dotnet restore
dotnet run
```

**Frontend (React + Vite)**

```bash
cd frontend
npm install
npm run dev
```

Copy `frontend/.env.example` to `frontend/.env` and set `VITE_API_BASE_URL=http://localhost:5072/api`.

## License

MIT
