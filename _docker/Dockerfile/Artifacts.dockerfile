# ============= Plugin files build =============
FROM debian:12-slim AS build

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential clang cmake ninja-build git unzip pkg-config \
    libx11-dev libxrandr-dev libxcursor-dev libxinerama-dev libxi-dev \
    libglu1-mesa-dev libasound2-dev libjack-jackd2-dev libcurl4-openssl-dev \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /build

# ============= Kubernetes plugin files access =============
FROM busybox:1.36 AS runtime

WORKDIR /artifacts

COPY vst_builds ./
