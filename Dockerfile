# syntax=docker/dockerfile:1

# ---------------------------------------------------------------- Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Nur die Projektdateien zuerst kopieren, damit das Restore-Layer gecacht wird.
COPY src/DESD.LifeHub.Core/DESD.LifeHub.Core.csproj src/DESD.LifeHub.Core/
COPY src/DESD.LifeHub.Infrastructure/DESD.LifeHub.Infrastructure.csproj src/DESD.LifeHub.Infrastructure/
COPY src/DESD.LifeHub.Web/DESD.LifeHub.Web.csproj src/DESD.LifeHub.Web/
RUN dotnet restore "src/DESD.LifeHub.Web/DESD.LifeHub.Web.csproj"

# Restliche Quellen kopieren und veröffentlichen.
COPY src/ src/
RUN dotnet publish "src/DESD.LifeHub.Web/DESD.LifeHub.Web.csproj" \
        -c Release \
        -o /app/publish \
        /p:UseAppHost=false

# ---------------------------------------------------------------- Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Die Anwendung lauscht im Container auf Port 8080.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# curl wird für den Container-Healthcheck benötigt.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Nicht als root betreiben.
RUN adduser --disabled-password --gecos "" --uid 10001 appuser

COPY --from=build /app/publish ./
USER appuser

ENTRYPOINT ["dotnet", "DESD.LifeHub.Web.dll"]
