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

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aqsat.Api.dll"]
