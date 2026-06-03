# ============= Base: .NET SDK 9.0 =============
FROM mcr.microsoft.com/dotnet/sdk:9.0.303 AS base

# ============= Build stage для headless VST3 =============
FROM debian:12-slim AS build

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential clang cmake ninja-build git unzip pkg-config \
    libx11-dev libxrandr-dev libxcursor-dev libxinerama-dev libxi-dev \
    libglu1-mesa-dev libasound2-dev libjack-jackd2-dev libcurl4-openssl-dev \
 && rm -rf /var/lib/apt/lists/*

# ============= Runtime/Dev Stage =============
FROM base AS dev

WORKDIR /app
EXPOSE 5001

# Volume для hot-reload разработки
VOLUME ["/app", "/plugins"]

CMD ["dotnet", "watch", "run", "--project", "/app"]

# ============= Build/Runtime Stage =============
FROM base AS publish

WORKDIR /src
COPY app/ app/

RUN dotnet restore app/ComposeNowPlugins.csproj
RUN dotnet publish app/ComposeNowPlugins.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    libasound2 libcurl4 libx11-6 libxrandr2 libxcursor1 libxinerama1 libxi6 libglu1-mesa \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /out .
ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
VOLUME ["/plugins", "/logs"]
ENTRYPOINT ["dotnet", "ComposeNowPlugins.dll"]
