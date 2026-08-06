# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TourMonitor.slnx ./
COPY src/TourMonitor/TourMonitor.csproj src/TourMonitor/
RUN dotnet restore src/TourMonitor/TourMonitor.csproj

COPY src/ src/
RUN dotnet publish src/TourMonitor/TourMonitor.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        curl \
        ca-certificates \
        libgtk-3-0 \
        libdbus-glib-1-2 \
        libasound2t64 \
        libnss3 \
        libnspr4 \
        libatk1.0-0 \
        libatk-bridge2.0-0 \
        libcups2 \
        libdrm2 \
        libxkbcommon0 \
        libxcomposite1 \
        libxdamage1 \
        libxfixes3 \
        libxrandr2 \
        libgbm1 \
        libpango-1.0-0 \
        libcairo2 \
        libglib2.0-0 \
        libx11-6 \
        libx11-xcb1 \
        libxcb1 \
        libxext6 \
        libxi6 \
        libxtst6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV Camoufox__InstallDirectory=/opt/camoufox
EXPOSE 8080

ENTRYPOINT ["dotnet", "TourMonitor.dll"]
