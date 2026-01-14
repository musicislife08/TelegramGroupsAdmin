#!/bin/bash
# =============================================================================
# PostgreSQL 17 to 18 Upgrade Script
# =============================================================================
# This script automates the upgrade from PostgreSQL 17 to 18 for Docker deployments.
#
# What it does:
#   1. Backs up the entire database using pg_dumpall
#   2. Stops the running containers
#   3. Updates compose.yml (image version + volume mount path)
#   4. Starts PostgreSQL 18 with fresh data directory
#   5. Restores the backup
#   6. Starts all services in detached mode
#
# Prerequisites:
#   - Docker and Docker Compose installed
#   - compose.yml in current directory (or parent)
#   - PostgreSQL 17 container currently running
#
# Usage:
#   ./scripts/upgrade-postgres-17-to-18.sh
#   ./scripts/upgrade-postgres-17-to-18.sh -y    # Skip confirmation prompt
#
# =============================================================================

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration - adjust these if your setup differs
COMPOSE_FILE="${COMPOSE_FILE:-compose.yml}"
POSTGRES_SERVICE="${POSTGRES_SERVICE:-postgres}"
POSTGRES_USER="${POSTGRES_USER:-tgadmin}"
BACKUP_FILE="pg17_backup_$(date +%Y%m%d_%H%M%S).sql"
SKIP_CONFIRM=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -y|--yes)
            SKIP_CONFIRM=true
            shift
            ;;
        *)
            shift
            ;;
    esac
done

log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Find compose file
find_compose_file() {
    if [[ -f "$COMPOSE_FILE" ]]; then
        return 0
    elif [[ -f "compose.yml" ]]; then
        COMPOSE_FILE="compose.yml"
    elif [[ -f "docker-compose.yml" ]]; then
        COMPOSE_FILE="docker-compose.yml"
    elif [[ -f "../compose.yml" ]]; then
        COMPOSE_FILE="../compose.yml"
    else
        log_error "Could not find compose file. Set COMPOSE_FILE environment variable."
        exit 1
    fi
    log_info "Using compose file: $COMPOSE_FILE"
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."

    # Check Docker
    if ! command -v docker &> /dev/null; then
        log_error "Docker is not installed or not in PATH"
        exit 1
    fi

    # Check if postgres container is running
    if ! docker compose -f "$COMPOSE_FILE" ps --status running | grep -q "$POSTGRES_SERVICE"; then
        log_error "PostgreSQL container is not running. Start it first with: docker compose up -d $POSTGRES_SERVICE"
        exit 1
    fi

    # Verify it's PostgreSQL 17
    PG_VERSION=$(docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" psql -U "$POSTGRES_USER" -d postgres -t -c "SHOW server_version;" 2>/dev/null | tr -d ' ' | cut -d'.' -f1)
    if [[ "$PG_VERSION" != "17" ]]; then
        log_error "Expected PostgreSQL 17, but found version $PG_VERSION"
        exit 1
    fi

    log_info "Found PostgreSQL $PG_VERSION running"
}

# Create backup
create_backup() {
    log_info "Creating database backup: $BACKUP_FILE"
    log_info "This may take a while for large databases..."

    docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
        pg_dumpall -U "$POSTGRES_USER" > "$BACKUP_FILE"

    BACKUP_SIZE=$(du -h "$BACKUP_FILE" | cut -f1)
    log_info "Backup complete: $BACKUP_FILE ($BACKUP_SIZE)"
}

# Stop postgres container only
stop_postgres() {
    log_info "Stopping PostgreSQL container..."
    docker compose -f "$COMPOSE_FILE" stop "$POSTGRES_SERVICE"
    docker compose -f "$COMPOSE_FILE" rm -f "$POSTGRES_SERVICE"
}

# Update compose file
update_compose_file() {
    log_info "Updating compose file for PostgreSQL 18..."

    # Backup original compose file
    cp "$COMPOSE_FILE" "${COMPOSE_FILE}.backup"
    log_info "Original compose file backed up to: ${COMPOSE_FILE}.backup"

    # Update image version: postgres:17-alpine -> postgres:18-alpine
    # Also handles postgres:17 without -alpine suffix
    sed -i.tmp 's/postgres:17-alpine/postgres:18-alpine/g' "$COMPOSE_FILE"
    sed -i.tmp 's/postgres:17$/postgres:18/g' "$COMPOSE_FILE"

    # Update volume mount path: /var/lib/postgresql/data -> /var/lib/postgresql
    # PostgreSQL 18 uses version-specific subdirectories (18/data)
    sed -i.tmp 's|/var/lib/postgresql/data|/var/lib/postgresql|g' "$COMPOSE_FILE"

    # Clean up temp files from sed
    rm -f "${COMPOSE_FILE}.tmp"

    log_info "Compose file updated"
}

# Pull new image
pull_new_image() {
    log_info "Pulling PostgreSQL 18 image..."
    docker pull postgres:18-alpine
}

# Clear old data volume (required for fresh PG18 init)
prepare_volumes() {
    log_info "Preparing volumes for PostgreSQL 18..."
    log_warn "The old PostgreSQL 17 data will be preserved in the backup file."

    # Find the volume/bind mount path from compose file
    # This handles both named volumes and bind mounts

    # For bind mounts like ./db:/var/lib/postgresql
    # Search entire compose file for bind mounts to postgresql data directory
    BIND_MOUNT=$(grep -E "^\s*-\s*\./[^:]+:/var/lib/postgresql" "$COMPOSE_FILE" | head -1 | sed 's/.*- //' | sed 's/:.*//' || true)

    if [[ -n "$BIND_MOUNT" && -d "$BIND_MOUNT" ]]; then
        log_info "Found bind mount: $BIND_MOUNT"

        # Rename old data directory instead of deleting
        if [[ -d "$BIND_MOUNT" ]]; then
            OLD_DATA_BACKUP="${BIND_MOUNT}_pg17_$(date +%Y%m%d_%H%M%S)"
            log_info "Moving old data directory to: $OLD_DATA_BACKUP"
            mv "$BIND_MOUNT" "$OLD_DATA_BACKUP"
        fi

        # Create fresh directory
        mkdir -p "$BIND_MOUNT"
        log_info "Created fresh data directory: $BIND_MOUNT"
    else
        # For named volumes, we need to remove and recreate
        log_warn "Using named volume - will need to remove old volume"

        # Get volume name (simplified - assumes format like 'volumename:/var/lib/postgresql')
        VOLUME_NAME=$(grep -A10 "$POSTGRES_SERVICE:" "$COMPOSE_FILE" | grep -E "^\s*-\s*[a-zA-Z].*:/var/lib/postgresql" | head -1 | sed 's/.*- //' | cut -d: -f1 || true)

        if [[ -n "$VOLUME_NAME" ]]; then
            log_info "Removing old named volume: $VOLUME_NAME"
            docker volume rm "$VOLUME_NAME" 2>/dev/null || true
        fi
    fi
}

# Start PostgreSQL 18
start_postgres() {
    log_info "Starting PostgreSQL 18..."
    docker compose -f "$COMPOSE_FILE" up -d "$POSTGRES_SERVICE"

    # Wait for PostgreSQL to be fully ready (not just accepting connections)
    log_info "Waiting for PostgreSQL 18 to be ready..."
    for i in {1..60}; do
        # pg_isready just checks socket - we need to verify we can actually query
        if docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
            psql -U "$POSTGRES_USER" -d postgres -c "SELECT 1" &>/dev/null; then
            log_info "PostgreSQL 18 is ready and accepting queries"
            # Extra wait for any post-init scripts to complete
            sleep 3
            return 0
        fi
        sleep 1
    done

    log_error "PostgreSQL 18 failed to start within 60 seconds"
    exit 1
}

# Restore backup
restore_backup() {
    log_info "Restoring database from backup..."
    log_info "This may take a while for large databases..."

    # psql will show errors for existing roles/databases, which is fine
    docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
        psql -U "$POSTGRES_USER" -d postgres < "$BACKUP_FILE" 2>&1 | grep -v "already exists" || true

    log_info "Database restore complete"
}


# Verify upgrade
verify_upgrade() {
    log_info "Verifying upgrade..."

    NEW_VERSION=$(docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" psql -U "$POSTGRES_USER" -d postgres -t -c "SHOW server_version;" 2>/dev/null | tr -d ' ')

    if [[ "$NEW_VERSION" == 18* ]]; then
        log_info "Successfully upgraded to PostgreSQL $NEW_VERSION"
    else
        log_error "Upgrade verification failed. Current version: $NEW_VERSION"
        exit 1
    fi
}

# Cleanup
cleanup() {
    log_info "Cleanup options:"
    echo "  - Backup file preserved: $BACKUP_FILE"
    echo "  - Original compose file: ${COMPOSE_FILE}.backup"
    echo ""
    echo "Once you've verified everything works, you can delete these files:"
    echo "  rm $BACKUP_FILE"
    echo "  rm ${COMPOSE_FILE}.backup"

    # If we moved old data directory, mention it
    if [[ -n "${OLD_DATA_BACKUP:-}" ]]; then
        echo "  rm -rf $OLD_DATA_BACKUP"
    fi
}

# Main execution
main() {
    echo "============================================="
    echo "PostgreSQL 17 to 18 Upgrade Script"
    echo "============================================="
    echo ""

    find_compose_file
    check_prerequisites

    echo ""
    log_warn "This script will:"
    echo "  1. Backup your database (pg_dumpall)"
    echo "  2. Stop the PostgreSQL container"
    echo "  3. Update compose.yml for PostgreSQL 18"
    echo "  4. Start PostgreSQL 18 with fresh data directory"
    echo "  5. Restore your database"
    echo ""

    if [[ "$SKIP_CONFIRM" != "true" ]]; then
        read -p "Continue? (y/N) " -n 1 -r
        echo ""

        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            log_info "Upgrade cancelled"
            exit 0
        fi
    else
        log_info "Skipping confirmation (--yes flag)"
    fi

    echo ""
    create_backup
    stop_postgres
    pull_new_image
    update_compose_file
    prepare_volumes
    start_postgres
    restore_backup
    verify_upgrade

    echo ""
    echo "============================================="
    log_info "Upgrade complete!"
    echo "============================================="
    echo ""

    cleanup
}

# Run main function
main "$@"
