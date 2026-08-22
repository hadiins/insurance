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
RUN dotnet publish src/Aqsat.Api/Aqsat.Api.csproj -c Release -o /app --no-restore

# One container serves both API and frontend (docs/TASKS.md Task 20) — the built Vite assets land
# in wwwroot, where Program.cs's UseStaticFiles/MapFallbackToFile already know to find them.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# curl backs docker-compose.prod.yml's healthcheck — not in the base runtime image by default.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY --from=frontend-build /web/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aqsat.Api.dll"]
