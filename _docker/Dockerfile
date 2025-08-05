# ============= Base: .NET SDK 9.0 =============
FROM mcr.microsoft.com/dotnet/sdk:9.0.303 AS base
WORKDIR /app

# ============= Build stage для headless VST3 =============
FROM base AS build

# Ставим тяжелые пакеты только для сборки
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        build-essential \
        clang \
        cmake \
        ninja-build \
        git \
        unzip \
        pkg-config \
        libx11-dev \
        libxrandr-dev \
        libxcursor-dev \
        libxinerama-dev \
        libxi-dev \
        libglu1-mesa-dev \
        libasound2-dev \
        libjack-jackd2-dev \
        libcurl4-openssl-dev \
        && apt-get clean && rm -rf /var/lib/apt/lists/*

# Пример сборки headless-плагина VST3:
# COPY /plugins/MyPlugin /build/MyPlugin
# RUN cmake -S /build/MyPlugin -B /build/MyPlugin/build \
#     && cmake --build /build/MyPlugin/build --config Release

# ============= Runtime/Dev Stage =============
FROM base AS dev

WORKDIR /app
EXPOSE 5001

# Volume для hot-reload разработки
VOLUME ["/app", "/plugins"]

CMD ["dotnet", "watch", "run", "--project", "/app"]
