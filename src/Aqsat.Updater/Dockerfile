FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/Aqsat.Updater/Aqsat.Updater.csproj src/Aqsat.Updater/
RUN dotnet restore src/Aqsat.Updater/Aqsat.Updater.csproj

COPY src/Aqsat.Updater/ src/Aqsat.Updater/
RUN dotnet publish src/Aqsat.Updater/Aqsat.Updater.csproj -c Release -o /app --no-restore

# docs/UPDATE-SYSTEM.md §1 — this is the ONLY container in the whole system that mounts
# /var/run/docker.sock (see docker-compose.prod.yml). It deliberately does not derive from or
# depend on anything in src/Aqsat.Api — no shared base image beyond the stock ASP.NET runtime, no
# project reference, so there is no path by which the internet-facing API could reach the socket.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# The docker.sock on the host is owned by root with a group whose GID varies by distro/installer
# (Docker's official packages use 999; rootless installs differ). The socket's group grants
# read/write, so joining a group with that GID lets the unprivileged 'app' user talk to Docker
# without running as root. Override at build time when the host GID differs:
#   DOCKER_GID=1234 docker compose -f docker-compose.prod.yml build updater
ARG DOCKER_GID=999
RUN groupadd -g ${DOCKER_GID} dockersock 2>/dev/null || true \
    && usermod -aG dockersock app \
    && mkdir -p /app/logs && chown -R app:app /app/logs
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8081
ENTRYPOINT ["dotnet", "Aqsat.Updater.dll"]
