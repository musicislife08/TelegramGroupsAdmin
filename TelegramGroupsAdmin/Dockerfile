# =============================================================================
# TelegramGroupsAdmin - Production Dockerfile
# Multi-stage build using Ubuntu Chiseled runtime for minimal attack surface
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build & Publish
# Use full SDK image for building and publishing
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Install wget for health checks (smaller than curl, fewer dependencies)
RUN apt-get update && apt-get install -y --no-install-recommends wget && rm -rf /var/lib/apt/lists/*

# Copy solution and project files (for better layer caching)
COPY ["TelegramGroupsAdmin.sln", "./"]
COPY ["TelegramGroupsAdmin/TelegramGroupsAdmin.csproj", "TelegramGroupsAdmin/"]
COPY ["TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj", "TelegramGroupsAdmin.Configuration/"]
COPY ["TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj", "TelegramGroupsAdmin.Core/"]
COPY ["TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj", "TelegramGroupsAdmin.Data/"]
COPY ["TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj", "TelegramGroupsAdmin.Telegram/"]
COPY ["TelegramGroupsAdmin.Telegram.Abstractions/TelegramGroupsAdmin.Telegram.Abstractions.csproj", "TelegramGroupsAdmin.Telegram.Abstractions/"]
COPY ["TelegramGroupsAdmin.ContentDetection/TelegramGroupsAdmin.ContentDetection.csproj", "TelegramGroupsAdmin.ContentDetection/"]

# Restore dependencies (cached layer if project files unchanged)
RUN dotnet restore "TelegramGroupsAdmin/TelegramGroupsAdmin.csproj"

# Copy all source code
COPY . .

# Publish
WORKDIR /src/TelegramGroupsAdmin
RUN dotnet publish "./TelegramGroupsAdmin.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:PublishTrimmed=false \
    /p:PublishSingleFile=false

# -----------------------------------------------------------------------------
# Stage 2: Runtime (Temporarily using full runtime for debugging)
# TODO: Switch back to chiseled-extra after debugging
# -----------------------------------------------------------------------------
# FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Arguments for user/group ID customization
ARG PUID=1654
ARG PGID=1654

# Copy wget binary and minimal dependencies from SDK stage for health checks
# wget is smaller and has fewer dependencies than curl
COPY --from=build /usr/bin/wget /usr/bin/wget
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt

# Create non-root user with customizable UID/GID
# Chiseled images use UID 1654 by default (APP_UID)
# Override via build args: --build-arg PUID=1000 --build-arg PGID=1000
USER $PUID:$PGID

WORKDIR /app

# Copy published application from build stage
COPY --from=build /app/publish .


# Create volume mount points for persistent data
# These will be mounted from host at runtime
# Note: Using /data instead of /app/data to avoid conflicts with published files
VOLUME ["/data"]

# .NET needs writable temp directories even in read-only containers
# These paths are writable by default in standard runtime
# /tmp - for temporary files
# /app/data - for application data (mounted volume)

# Expose HTTP port (HTTPS handled by reverse proxy)
EXPOSE 8080

# Health check using wget copied from SDK stage
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD ["/usr/bin/wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/health"]

# Set environment variables for production
# TZ can be overridden at runtime: -e TZ=America/Phoenix
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=UTC \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Entrypoint
ENTRYPOINT ["dotnet", "TelegramGroupsAdmin.dll"]

# =============================================================================
# Build Instructions:
# docker build -t telegramgroupsadmin:latest -f TelegramGroupsAdmin/Dockerfile .
#
# Run Instructions:
# docker run -d \
#   --name telegram-groups-admin \
#   -p 8080:8080 \
#   -v /path/to/data:/app/data \
#   -e TELEGRAM__BOTTOKEN=your_token \
#   -e OPENAI__APIKEY=your_key \
#   ... (all other env vars) \
#   telegramgroupsadmin:latest
#
# Security Features:
# - Ubuntu Chiseled runtime (minimal, no shell, no package manager)
# - Non-root user (APP_UID)
# - No unnecessary packages or tools
# - Read-only filesystem (except /app/data volume)
# - Diagnostics disabled in production
# - Single HTTP port exposure (HTTPS via reverse proxy)
# =============================================================================
