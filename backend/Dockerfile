# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["backend.csproj", "./"]
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/data

EXPOSE 5072

ENV ASPNETCORE_URLS=http://+:5072
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_DIR=/app/data

ENTRYPOINT ["dotnet", "backend.dll"]
