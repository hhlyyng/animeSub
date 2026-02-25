# Build stage — always runs on the build machine (amd64), cross-compiles for target arch
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY ["backend.csproj", "./"]
RUN dotnet restore -a $TARGETARCH

COPY . .
RUN dotnet publish -c Release -o /app/publish -a $TARGETARCH --no-restore

# Runtime stage — uses the correct base image for the target arch
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/data

EXPOSE 5072

ENV ASPNETCORE_URLS=http://+:5072
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_DIR=/app/data

ENTRYPOINT ["dotnet", "backend.dll"]
