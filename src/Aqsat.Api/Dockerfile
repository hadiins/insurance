FROM node:20-alpine AS frontend-build
WORKDIR /web
COPY src/aqsat-web/package*.json ./
RUN npm ci
COPY src/aqsat-web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/Aqsat.Domain/Aqsat.Domain.csproj src/Aqsat.Domain/
COPY src/Aqsat.Application/Aqsat.Application.csproj src/Aqsat.Application/
COPY src/Aqsat.Infrastructure/Aqsat.Infrastructure.csproj src/Aqsat.Infrastructure/
COPY src/Aqsat.Api/Aqsat.Api.csproj src/Aqsat.Api/
RUN dotnet restore src/Aqsat.Api/Aqsat.Api.csproj

COPY src/Aqsat.Domain/ src/Aqsat.Domain/
COPY src/Aqsat.Application/ src/Aqsat.Application/
COPY src/Aqsat.Infrastructure/ src/Aqsat.Infrastructure/
COPY src/Aqsat.Api/ src/Aqsat.Api/
# APP_VERSION (optional build-arg, e.g. 1.1.0) is stamped into the assembly's InformationalVersion.
# PlatformUpdatesController reads the version from the image itself, never from a runtime env var:
# Aqsat.Updater recreates this container inheriting the OLD container's env, so an env-based version
# would stay stale forever after the first panel-driven update (breaking every later version gate,
# which fails closed on it). Unstamped builds report "unknown" — honest about being unreleased.
ARG APP_VERSION=""
RUN if [ -n "$APP_VERSION" ]; then \
      dotnet publish src/Aqsat.Api/Aqsat.Api.csproj -c Release -o /app --no-restore -p:Version="$APP_VERSION"; \
    else \
      dotnet publish src/Aqsat.Api/Aqsat.Api.csproj -c Release -o /app --no-restore -p:InformationalVersion="unknown"; \
    fi

# One container serves both API and frontend (docs/TASKS.md Task 20) — the built Vite assets land
# in wwwroot, where Program.cs's UseStaticFiles/MapFallbackToFile already know to find them.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# curl backs docker-compose.prod.yml's healthcheck — not in the base runtime image by default.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY --from=frontend-build /web/dist ./wwwroot
# Serilog writes logs/aqsat-api-.log relative to /app; pre-create it and hand it (only it) to the
# image's built-in unprivileged 'app' user, then drop root entirely.
RUN mkdir -p /app/logs && chown -R app:app /app/logs
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aqsat.Api.dll"]
