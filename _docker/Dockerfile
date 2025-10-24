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
