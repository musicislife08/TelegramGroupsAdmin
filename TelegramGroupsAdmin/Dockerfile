# =============================================================================
# TelegramGroupsAdmin - Production Dockerfile
# Multi-stage build using Ubuntu Chiseled runtime for minimal attack surface
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build & Publish
# Use full SDK image for building and publishing
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Install build dependencies
# wget: for health checks (smaller than curl, fewer dependencies)
# tesseract-ocr, libleptonica-dev: for OCR-based image spam detection (ML-5)
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    tesseract-ocr \
    libleptonica-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy solution and project files (for better layer caching)
# Excludes TelegramGroupsAdmin.Tests - not needed for production runtime
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

# Download Tesseract English language data (tessdata_best for accuracy)
# ML-5: OCR-based image spam detection
RUN wget -q https://github.com/tesseract-ocr/tessdata_best/raw/main/eng.traineddata \
    -O /tmp/eng.traineddata && \
    echo "Downloaded eng.traineddata: $(du -h /tmp/eng.traineddata | cut -f1)"

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
# Stage 2: Runtime (Ubuntu Chiseled - minimal attack surface)
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled-extra AS final

# Arguments for user/group ID customization
ARG PUID=1654
ARG PGID=1654

# Copy wget binary and minimal dependencies from SDK stage for health checks
# wget is smaller and has fewer dependencies than curl
COPY --from=build /usr/bin/wget /usr/bin/wget
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt

# Copy Tesseract OCR binaries and libraries from build stage (ML-5: OCR spam detection)
# Tesseract binary
COPY --from=build /usr/bin/tesseract /usr/bin/tesseract
# Leptonica and Tesseract libraries (multi-arch support: aarch64, x86_64)
COPY --from=build /usr/lib/*/liblept* /usr/lib/
COPY --from=build /usr/lib/*/libtesseract* /usr/lib/
# Additional dependencies (image format libraries)
COPY --from=build /usr/lib/*/libpng* /usr/lib/
COPY --from=build /usr/lib/*/libjpeg* /usr/lib/
COPY --from=build /usr/lib/*/libtiff* /usr/lib/
COPY --from=build /usr/lib/*/libwebp* /usr/lib/
COPY --from=build /usr/lib/*/libgif* /usr/lib/
COPY --from=build /usr/lib/*/libopenjp2* /usr/lib/
# Copy all runtime dependencies to /usr/lib (flattened structure)
COPY --from=build /usr/lib/*/lib*.so.* /usr/lib/

# Copy Tesseract language data from build stage (ML-5: OCR spam detection)
# 14.7MB - tessdata_best for better accuracy on compressed spam images
COPY --from=build --chown=$PUID:$PGID /tmp/eng.traineddata /tessdata/eng.traineddata

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
# /data - user data (media, keys, etc.)
# /tessdata - baked into image (NOT a volume), contains eng.traineddata for OCR
VOLUME ["/data"]

# .NET needs writable temp directories even in read-only containers
# These paths are writable by default in standard runtime
# /tmp - for temporary files
# /app/data - for application data (mounted volume)

# Expose HTTP port (HTTPS handled by reverse proxy)
EXPOSE 8080

# Health check using wget copied from SDK stage
# Use liveness probe (no dependency checks - database failures shouldn't cause container restart)
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["/usr/bin/wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/healthz/live"]

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
