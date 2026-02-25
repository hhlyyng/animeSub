# Frequently Asked Questions

## How do I get a TMDB API Token?

1. Register a free account at [TMDB](https://www.themoviedb.org/)
2. Go to [Account Settings → API](https://www.themoviedb.org/settings/api)
3. Request a **Developer** API key
4. After approval, find the **API Read Access Token** on that page (a long string starting with `eyJ`)
5. Paste it into the AnimeSub setup wizard (Step 3) or update it in the Settings page

> **Important**: You need the "API Read Access Token" (Bearer Token), **not** the shorter "API Key".

## Is the TMDB Token required?

No, it is optional. Without a TMDB token:

- Anime cards still display normally (from Bangumi)
- English titles and descriptions will be unavailable
- Landscape backdrop images will be unavailable (only portrait posters shown)
- AniList and MAL Top 10 rankings are unaffected

## qBittorrent connection fails — what should I check?

Troubleshoot in this order:

1. **Confirm WebUI is enabled**: Open qBittorrent → Tools → Options → Web UI → check "Enable the Web User Interface"
2. **Verify the port**: Default is 8080; update if you changed it
3. **Check the host address**:
   - Same machine as AnimeSub: use `localhost` or `127.0.0.1`
   - Different machine: use the LAN IP (e.g., `192.168.1.100`)
   - Inside Docker accessing the host: use `host.docker.internal` (Windows/Mac)
4. **Verify credentials**: Open `http://IP:8080` in your browser to confirm login works
5. **Check firewall**: Ensure port 8080 is not blocked

## How do I change the application port?

Edit the `ports` section in `docker-compose.yml`:

```yaml
ports:
  - "8888:80"   # The left number (8888) is the port you access in your browser
```

Then restart:

```bash
docker compose up -d
```

## Where is my data stored?

All persistent data is stored in the `./config` directory mapped by the Docker volume:

| File | Contents |
|------|----------|
| `./config/anime.db` | SQLite database (anime data, subscriptions, download history) |
| `./config/appsettings.runtime.json` | Runtime configuration (qBittorrent, API tokens, auth) |

## How do I reset my credentials?

Delete the runtime config file and restart. The system will return to the setup wizard:

```bash
# Stop the container
docker compose down

# Remove runtime config (keeps database intact)
rm ./config/appsettings.runtime.json

# Restart
docker compose up -d
```

Visit `http://localhost:3000` — the setup wizard will appear.

> **Note**: This does not delete the database (subscriptions and download history are preserved). To fully reset, delete the entire `./config` directory.

## Subscriptions are not auto-downloading — how do I debug?

Check in this order:

1. **Confirm the subscription is enabled** (check the subscriptions list page)
2. **Confirm RSS polling is on**: Check `EnablePolling: true` in settings
3. **Trigger a manual check**: Click the "Check" button next to the subscription to see if new resources appear
4. **Verify qBittorrent connectivity**: Click "Test Connection" in settings
5. **Check if keyword filters are too strict**: Try clearing include/exclude keywords and run a manual check
6. **View logs**: `docker compose logs animesub` to see detailed error output

## How do I view application logs?

```bash
# Stream live logs
docker compose logs -f animesub

# View the last 100 lines
docker compose logs --tail=100 animesub
```

## Can I run multiple instances?

Not recommended. Multiple instances sharing the same SQLite file may cause write conflicts. If you need high availability, consider migrating the database to PostgreSQL (requires code changes).

## How do I update to a new version?

```bash
# Pull the latest image
docker compose pull

# Restart the service (data is not affected)
docker compose up -d
```
