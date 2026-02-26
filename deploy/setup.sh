#!/bin/bash
set -e

echo "==> Creating /opt/animesub directory..."
mkdir -p /opt/animesub && cd /opt/animesub

echo "==> Writing docker-compose.yml..."
cat > docker-compose.yml << 'COMPOSE'
services:
  backend:
    image: ghcr.io/hhlyyng/anime-subscription-backend:latest
    restart: unless-stopped
    volumes:
      - ./config:/app/data
    environment:
      - DATA_DIR=/app/data
      - ASPNETCORE_ENVIRONMENT=Production
      - CORS_ORIGINS=*
    networks:
      - anime_net

  frontend:
    image: ghcr.io/hhlyyng/anime-subscription-frontend:latest
    restart: unless-stopped
    ports:
      - "8000:80"
    environment:
      - BACKEND_HOST=backend
      - BACKEND_PORT=5072
      - NGINX_ENVSUBST_FILTER=BACKEND_HOST|BACKEND_PORT
    depends_on:
      - backend
    networks:
      - anime_net

networks:
  anime_net:
COMPOSE

echo "==> Pulling images..."
docker compose pull

echo "==> Starting services..."
docker compose up -d

echo "==> Status:"
docker compose ps

echo ""
echo "Done! Access at http://$(hostname -I | awk '{print $1}'):8000"
