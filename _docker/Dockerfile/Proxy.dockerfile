# ============= Base: .NET SDK 9.0 =============
FROM mcr.microsoft.com/dotnet/sdk:9.0.303 AS base

# ============= Local development =============
FROM base AS dev

WORKDIR /workspace
EXPOSE 5001
VOLUME ["/workspace", "/logs"]

CMD ["dotnet", "--info"]

# ============= Production build =============
FROM base AS publish

WORKDIR /workspace
COPY src/ ./src/

RUN dotnet restore src/Proxy/Proxy.csproj
RUN dotnet publish src/Proxy/Proxy.csproj -c Release -o /out --no-restore

# ============= Production runtime =============
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app
COPY --from=publish /out ./

ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
VOLUME ["/logs"]

ENTRYPOINT ["dotnet", "Proxy.dll"]
