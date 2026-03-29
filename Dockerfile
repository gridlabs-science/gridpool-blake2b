FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY boot_portal/boot_portal.csproj boot_portal/
RUN dotnet restore boot_portal/boot_portal.csproj

COPY . .
RUN dotnet publish boot_portal/boot_portal.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libsodium23 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV BOOT_PORTAL_CONFIG_PATH=/data/boot_portal_config.json
ENV BOOT_PORTAL_STATE_PATH=/data/pool_state.json

VOLUME ["/data"]

EXPOSE 5000 3008

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "boot_portal.dll"]
