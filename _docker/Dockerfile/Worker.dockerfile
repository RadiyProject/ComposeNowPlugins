# ============= Base: .NET SDK 9.0 =============
FROM mcr.microsoft.com/dotnet/sdk:9.0.303 AS base

# ============= Local development =============
FROM base AS dev

WORKDIR /workspace
EXPOSE 5002
VOLUME ["/workspace", "/plugins", "/logs"]

CMD ["dotnet", "--info"]

# ============= Production build =============
FROM base AS publish

WORKDIR /workspace
COPY Directory.Build.props NuGet.Config ./
COPY src/ ./src/

RUN dotnet restore src/Worker/Worker.csproj
RUN dotnet publish src/Worker/Worker.csproj -c Release -o /out --no-restore

# ============= Production runtime =============
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    libasound2 libcurl4 libx11-6 libxrandr2 libxcursor1 libxinerama1 libxi6 libglu1-mesa \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /out ./

ENV ASPNETCORE_URLS=http://+:5002
EXPOSE 5002
VOLUME ["/plugins", "/logs"]

ENTRYPOINT ["dotnet", "Worker.dll"]
