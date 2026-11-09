# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a source change does not re-download packages.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/OpenTaan.Protocol/OpenTaan.Protocol.csproj src/OpenTaan.Protocol/
COPY src/OpenTaan.Transport/OpenTaan.Transport.csproj src/OpenTaan.Transport/
COPY src/OpenTaan.GameServer/OpenTaan.GameServer.csproj src/OpenTaan.GameServer/
RUN dotnet restore src/OpenTaan.GameServer/OpenTaan.GameServer.csproj

COPY src/ src/
RUN dotnet publish src/OpenTaan.GameServer/OpenTaan.GameServer.csproj \
    -c Release --no-restore -o /app

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# The base image can lag behind Ubuntu's security fixes; take the patched apt
# rather than wait for the next upstream rebuild.
RUN apt-get update \
 && apt-get install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# The .NET images ship a non-root user; the game server needs no privileges.
USER $APP_UID
EXPOSE 2180
ENTRYPOINT ["dotnet", "OpenTaan.GameServer.dll"]
