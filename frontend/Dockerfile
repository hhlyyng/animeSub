# Build stage
FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .

# VITE_API_BASE_URL is baked into the JS bundle at build time.
# When using docker-compose with nginx proxy, use /api (default).
# When exposing backend separately, set to http://your-host:5072/api
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# Runtime stage
FROM nginx:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
# nginx:alpine processes /etc/nginx/templates/*.template at startup via envsubst
COPY nginx.conf.template /etc/nginx/templates/default.conf.template

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
