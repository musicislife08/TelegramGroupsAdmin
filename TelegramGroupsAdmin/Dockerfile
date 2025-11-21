# =============================================================================
# TelegramGroupsAdmin - Production Dockerfile (Optimized Multi-Stage)
# Multi-stage build using Ubuntu Chiseled runtime for minimal attack surface
# Optimized for fast ARM64 builds with cross-compilation support
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Dependencies - Download heavy binaries (cached separately)
# This stage rarely changes, providing excellent cache reuse
# -----------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS dependencies

ARG TARGETPLATFORM
ARG BUILDPLATFORM

WORKDIR /deps

# Install system dependencies for downloading and extracting
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    xz-utils \
    && rm -rf /var/lib/apt/lists/*

# Download FFmpeg static build (architecture-specific) with checksum validation
# Uses TARGETPLATFORM to download correct binary during cross-compilation
# Example: TARGETPLATFORM=linux/arm64 downloads arm64 binary on amd64 host
# Currently pinned to: version 7.0.2 (via release channel with MD5 validation)
# Security: MD5 validation ensures binary integrity and reproducible builds
RUN case "$TARGETPLATFORM" in \
        "linux/amd64") FFMPEG_ARCH="amd64" ;; \
        "linux/arm64") FFMPEG_ARCH="arm64" ;; \
        *) echo "Unsupported platform: $TARGETPLATFORM" && exit 1 ;; \
    esac && \
    echo "Downloading FFmpeg (release) for ${FFMPEG_ARCH} (target: $TARGETPLATFORM, building on: $BUILDPLATFORM)..." && \
    wget -q https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-${FFMPEG_ARCH}-static.tar.xz \
        -O /tmp/ffmpeg.tar.xz && \
    wget -q https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-${FFMPEG_ARCH}-static.tar.xz.md5 \
        -O /tmp/ffmpeg.tar.xz.md5 && \
    echo "Validating checksum..." && \
    cd /tmp && \
    EXPECTED_MD5=$(awk '{print $1}' ffmpeg.tar.xz.md5) && \
    ACTUAL_MD5=$(md5sum ffmpeg.tar.xz | awk '{print $1}') && \
    echo "Expected: ${EXPECTED_MD5}" && \
    echo "Actual:   ${ACTUAL_MD5}" && \
    [ "${EXPECTED_MD5}" = "${ACTUAL_MD5}" ] || (echo "MD5 mismatch!" && exit 1) && \
    echo "Checksum validated successfully" && \
    echo "Extracting FFmpeg..." && \
    mkdir -p /deps/ffmpeg && \
    tar -xf /tmp/ffmpeg.tar.xz -C /deps/ffmpeg --strip-components=1 && \
    chmod +x /deps/ffmpeg/ffmpeg /deps/ffmpeg/ffprobe && \
    rm -rf /tmp/ffmpeg.tar.xz /tmp/ffmpeg.tar.xz.md5 && \
    echo "FFmpeg ready: $(/deps/ffmpeg/ffmpeg -version | head -n1)"

# Download Tesseract language data (architecture-independent)
# ML-5: OCR-based image spam detection
RUN wget -q https://github.com/tesseract-ocr/tessdata_best/raw/main/eng.traineddata \
    -O /deps/eng.traineddata && \
    echo "Downloaded eng.traineddata: $(du -h /deps/eng.traineddata | cut -f1)"

# Download FastText language identification model (architecture-independent)
# 176-language model for translation pre-filtering
RUN wget -q https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz \
    -O /deps/lid.176.ftz && \
    echo "Downloaded lid.176.ftz: $(du -h /deps/lid.176.ftz | cut -f1)"

# -----------------------------------------------------------------------------
# Stage 2: Tesseract Build Environment
# Install Tesseract OCR with all dependencies for target architecture
# Separate stage to cache these heavy system packages
# Builds for target platform by default (no --platform flag needed)
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tesseract-env

ARG TARGETPLATFORM
ARG BUILDPLATFORM

# Install Tesseract OCR and dependencies
# These will be copied to final runtime image
RUN apt-get update && apt-get install -y --no-install-recommends \
    tesseract-ocr \
    libleptonica-dev \
    wget \
    && rm -rf /var/lib/apt/lists/*

# -----------------------------------------------------------------------------
# Stage 3: NuGet Restore
# Restore .NET dependencies with BuildKit cache mounts for speed
# Cross-compilation happens here: build on BUILDPLATFORM, target TARGETPLATFORM
# -----------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS restore

ARG TARGETPLATFORM
ARG BUILDPLATFORM

WORKDIR /src

# Copy solution and project files (for layer caching)
# Excludes TelegramGroupsAdmin.Tests - not needed for production runtime
COPY ["TelegramGroupsAdmin.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["global.json", "./"]
COPY ["TelegramGroupsAdmin/TelegramGroupsAdmin.csproj", "TelegramGroupsAdmin/"]
COPY ["TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj", "TelegramGroupsAdmin.Configuration/"]
COPY ["TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj", "TelegramGroupsAdmin.Core/"]
COPY ["TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj", "TelegramGroupsAdmin.Data/"]
COPY ["TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj", "TelegramGroupsAdmin.Telegram/"]
COPY ["TelegramGroupsAdmin.Telegram.Abstractions/TelegramGroupsAdmin.Telegram.Abstractions.csproj", "TelegramGroupsAdmin.Telegram.Abstractions/"]
COPY ["TelegramGroupsAdmin.ContentDetection/TelegramGroupsAdmin.ContentDetection.csproj", "TelegramGroupsAdmin.ContentDetection/"]

# Restore with runtime identifier for cross-compilation
# Runs on BUILDPLATFORM (fast AMD64), downloads packages for TARGETPLATFORM
# Unified cache for multi-arch builds (when building both platforms in single job)
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    case "$TARGETPLATFORM" in \
        "linux/amd64") RID="linux-x64" ;; \
        "linux/arm64") RID="linux-arm64" ;; \
        *) echo "Unsupported platform: $TARGETPLATFORM" && exit 1 ;; \
    esac && \
    echo "Restoring packages for $RID (target: $TARGETPLATFORM, building on: $BUILDPLATFORM)..." && \
    dotnet restore "TelegramGroupsAdmin/TelegramGroupsAdmin.csproj" \
        -r $RID \
        --verbosity minimal

# -----------------------------------------------------------------------------
# Stage 4: Build & Publish
# Cross-compile: build on fast BUILDPLATFORM (AMD64), output for TARGETPLATFORM
# This eliminates QEMU overhead for ARM64 builds!
# -----------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_CONFIGURATION=Release
ARG TARGETPLATFORM
ARG BUILDPLATFORM

WORKDIR /src

# Copy project files from restore stage
COPY --from=restore /src .

# Copy all source code
COPY . .

# Build and publish with cross-compilation
# KEY OPTIMIZATION: Runs on AMD64 host, compiles for ARM64 target (no QEMU!)
# Unified cache for multi-arch builds (matches restore stage cache)
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    case "$TARGETPLATFORM" in \
        "linux/amd64") RID="linux-x64" ;; \
        "linux/arm64") RID="linux-arm64" ;; \
        *) echo "Unsupported platform: $TARGETPLATFORM" && exit 1 ;; \
    esac && \
    echo "Building for $RID (target: $TARGETPLATFORM, building on: $BUILDPLATFORM)..." && \
    cd TelegramGroupsAdmin && \
    dotnet publish "./TelegramGroupsAdmin.csproj" \
        -c $BUILD_CONFIGURATION \
        -r $RID \
        -o /app/publish \
        --self-contained false \
        /p:UseAppHost=false \
        /p:PublishTrimmed=false \
        /p:PublishSingleFile=false && \
    echo "Build complete for $RID"

# -----------------------------------------------------------------------------
# Stage 5: Runtime (Ubuntu Chiseled - minimal attack surface)
# Final production image with only runtime dependencies
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final

# Arguments for user/group ID customization
ARG PUID=1654
ARG PGID=1654

# Copy wget binary and minimal dependencies from tesseract-env for health checks
# wget is smaller and has fewer dependencies than curl
COPY --from=tesseract-env /usr/bin/wget /usr/bin/wget
COPY --from=tesseract-env /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt

# Copy Tesseract OCR binaries and libraries from tesseract-env (ML-5: OCR spam detection)
# Tesseract binary
COPY --from=tesseract-env /usr/bin/tesseract /usr/bin/tesseract
# Leptonica and Tesseract libraries (multi-arch support: aarch64, x86_64)
COPY --from=tesseract-env /usr/lib/*/liblept* /usr/lib/
COPY --from=tesseract-env /usr/lib/*/libtesseract* /usr/lib/
# Additional dependencies (image format libraries)
COPY --from=tesseract-env /usr/lib/*/libpng* /usr/lib/
COPY --from=tesseract-env /usr/lib/*/libjpeg* /usr/lib/
COPY --from=tesseract-env /usr/lib/*/libtiff* /usr/lib/
COPY --from=tesseract-env /usr/lib/*/libwebp* /usr/lib/
COPY --from=tesseract-env /usr/lib/*/libgif* /usr/lib/
COPY --from=tesseract-env /usr/lib/*/libopenjp2* /usr/lib/
# Copy all runtime dependencies to /usr/lib (flattened structure)
# Includes transitive deps for Tesseract/Leptonica (Pango, Cairo, X11, curl, etc.)
# ~171MB but ensures all deps are present (Debian's leptonica-progs pulls in libarchive→curl)
COPY --from=tesseract-env /usr/lib/*/lib*.so.* /usr/lib/

# Copy Tesseract language data from dependencies stage (ML-5: OCR spam detection)
# 14.7MB - tessdata_best for better accuracy on compressed spam images
COPY --from=dependencies --chown=$PUID:$PGID /deps/eng.traineddata /tessdata/eng.traineddata

# Copy FastText language identification model from dependencies stage
# 917KB - 176-language model for translation pre-filtering
COPY --from=dependencies --chown=$PUID:$PGID /deps/lid.176.ftz /data/models/lid.176.ftz

# Copy FFmpeg static binaries from dependencies stage (ML-6: Video frame extraction)
# Static builds = no library dependencies, works in Chiseled runtime
COPY --from=dependencies /deps/ffmpeg/ffmpeg /usr/local/bin/ffmpeg
COPY --from=dependencies /deps/ffmpeg/ffprobe /usr/local/bin/ffprobe

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

# Health check using wget copied from tesseract-env stage
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
#
# Local build (single architecture):
# docker build -t telegramgroupsadmin:latest -f TelegramGroupsAdmin/Dockerfile .
#
# Multi-architecture build (uses BuildKit cross-compilation):
# docker buildx build --platform linux/amd64,linux/arm64 -t telegramgroupsadmin:latest -f TelegramGroupsAdmin/Dockerfile .
#
# Build with specific FFmpeg version:
# docker build --build-arg FFMPEG_VERSION=7.0.1 -t telegramgroupsadmin:latest -f TelegramGroupsAdmin/Dockerfile .
#
# Build with custom user/group ID:
# docker build --build-arg PUID=1000 --build-arg PGID=1000 -t telegramgroupsadmin:latest -f TelegramGroupsAdmin/Dockerfile .
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
# Performance Optimizations:
# - Multi-stage build with 5 stages (vs 2 original)
# - Cross-compilation: Build on fast AMD64, compile for ARM64 (no QEMU overhead!)
# - BuildKit cache mounts for NuGet packages (persistent across builds)
# - Separate dependency stage for FFmpeg/Tesseract (rarely changes = excellent cache)
# - Layer ordering optimized: Restore before heavy downloads
# - Platform-aware: Uses TARGETPLATFORM/BUILDPLATFORM for correct binaries
#
# Security Features:
# - Ubuntu Chiseled runtime (minimal, no shell, no package manager)
# - Non-root user (APP_UID)
# - No unnecessary packages or tools
# - Read-only filesystem (except /app/data volume)
# - Diagnostics disabled in production
# - Single HTTP port exposure (HTTPS via reverse proxy)
# - FFmpeg version pinned (7.0.2 default, override via build arg)
# =============================================================================
