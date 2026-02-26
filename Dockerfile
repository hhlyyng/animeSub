# Stage 1: Build frontend
FROM --platform=$BUILDPLATFORM node:20-alpine AS frontend-build
WORKDIR /app

COPY frontend/package*.json ./
RUN npm ci

COPY frontend/ .
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
RUN npm run build

# Stage 2: Build backend
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build
ARG TARGETARCH
WORKDIR /src

COPY backend/backend.csproj ./
RUN dotnet restore -a $TARGETARCH

COPY backend/ .
RUN dotnet publish -c Release -o /app/publish -a $TARGETARCH --no-restore

# Stage 3: Runtime — combine both
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy backend
COPY --from=backend-build /app/publish .

# Copy frontend build output into wwwroot
COPY --from=frontend-build /app/dist ./wwwroot

RUN mkdir -p /app/data

EXPOSE 5072

ENV ASPNETCORE_URLS=http://+:5072
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_DIR=/app/data
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

ENTRYPOINT ["dotnet", "backend.dll"]
