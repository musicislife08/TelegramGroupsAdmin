#!/bin/bash
# =============================================================================
# PostgreSQL 17 to 18 Upgrade Script
# =============================================================================
# This script automates the upgrade from PostgreSQL 17 to 18 for Docker deployments.
#
# What it does:
#   1. Backs up the entire database using pg_dumpall
#   2. Stops all services (docker compose down)
#   3. Updates compose.yml (image version + volume mount path)
#   4. Starts PostgreSQL 18 with fresh data directory
#   5. Restores the backup
#
# After completion, start your services manually:
#   docker compose up -d
#
# Features:
#   - Supports both bind mounts and named volumes (auto-detected)
#   - Automatic rollback on failure (retryable)
#   - Validates backup before proceeding
#   - Preserves all original files for manual recovery
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

set -Eeuo pipefail

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

# State tracking for rollback
STATE_COMPOSE_BACKED_UP=false
STATE_COMPOSE_MODIFIED=false
STATE_SERVICES_STOPPED=false
STATE_VOLUME_MODIFIED=false
STATE_PG18_STARTED=false

# Volume detection results (set by detect_volume_type)
VOLUME_TYPE=""           # "bind" or "named"
BIND_MOUNT_PATH=""       # Path for bind mounts (e.g., ./db)
NAMED_VOLUME_NAME=""     # Name for named volumes (e.g., postgres-data)
NEW_VOLUME_NAME=""       # New volume name for PG18 (e.g., postgres-data-pg18)
OLD_DATA_BACKUP=""       # Path where old bind mount data was moved

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

# Rollback function - restores system to pre-upgrade state
rollback() {
    echo ""
    log_error "Upgrade failed! Rolling back changes..."

    # Stop PG18 if we started it
    if [[ "$STATE_PG18_STARTED" == "true" ]]; then
        log_info "Stopping PostgreSQL 18..."
        docker compose -f "$COMPOSE_FILE" stop "$POSTGRES_SERVICE" 2>/dev/null || true
        docker compose -f "$COMPOSE_FILE" rm -f "$POSTGRES_SERVICE" 2>/dev/null || true
    fi

    # Restore compose file if we modified it
    if [[ "$STATE_COMPOSE_MODIFIED" == "true" && -f "${COMPOSE_FILE}.backup" ]]; then
        log_info "Restoring original compose file..."
        mv "${COMPOSE_FILE}.backup" "$COMPOSE_FILE"
    fi

    # Restore bind mount data if we moved it
    if [[ "$STATE_VOLUME_MODIFIED" == "true" && "$VOLUME_TYPE" == "bind" ]]; then
        if [[ -n "$OLD_DATA_BACKUP" && -d "$OLD_DATA_BACKUP" ]]; then
            log_info "Restoring original data directory..."
            # Remove the empty new directory we created
            if [[ -d "$BIND_MOUNT_PATH" ]]; then
                rmdir "$BIND_MOUNT_PATH" 2>/dev/null || rm -rf "$BIND_MOUNT_PATH"
            fi
            mv "$OLD_DATA_BACKUP" "$BIND_MOUNT_PATH"
        fi
    fi

    # For named volumes, old volume still exists (we created a new one)
    # Compose file restore points back to old volume - nothing else needed
    if [[ "$STATE_VOLUME_MODIFIED" == "true" && "$VOLUME_TYPE" == "named" ]]; then
        log_info "Original volume '$NAMED_VOLUME_NAME' preserved (new volume '$NEW_VOLUME_NAME' was created)"
        # Clean up the new PG18 volume if it was created
        if [[ -n "$NEW_VOLUME_NAME" ]]; then
            local full_new_volume
            full_new_volume=$(docker volume ls --format '{{.Name}}' | grep -E "(^|_)${NEW_VOLUME_NAME}$" | head -1 || true)
            if [[ -n "$full_new_volume" ]]; then
                log_info "Removing new volume: $full_new_volume"
                docker volume rm "$full_new_volume" 2>/dev/null || true
            fi
        fi
    fi

    echo ""
    log_info "Rollback complete. You can retry the upgrade after fixing the issue."
    log_info "Backup file preserved: $BACKUP_FILE"
    exit 1
}

# Set up trap for automatic rollback on error
trap 'rollback' ERR

# Find compose file
find_compose_file() {
    if [[ -f "$COMPOSE_FILE" ]]; then
        :  # Use as-is
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

# Detect volume type using docker compose config (reliable parsing)
detect_volume_type() {
    log_info "Detecting volume configuration..."

    # Use docker compose config to get normalized YAML
    # docker compose config outputs volumes in normalized format:
    #   volumes:
    #     - type: volume|bind
    #       source: volume-name or /path
    #       target: /var/lib/postgresql
    local config
    config=$(docker compose -f "$COMPOSE_FILE" config 2>/dev/null)

    # Find the volume entry for postgresql by looking for the target path
    # Then extract source and type from nearby lines
    local volume_block
    volume_block=$(echo "$config" | grep -B3 "target: /var/lib/postgresql" | head -4 || true)

    if [[ -z "$volume_block" ]]; then
        log_error "Could not find PostgreSQL volume mount in compose config"
        log_error "Expected volume mounted to /var/lib/postgresql"
        exit 1
    fi

    # Extract type (volume or bind)
    local vol_type
    vol_type=$(echo "$volume_block" | grep "type:" | sed 's/.*type: //' | tr -d ' ')

    # Extract source (volume name or path)
    local source
    source=$(echo "$volume_block" | grep "source:" | sed 's/.*source: //' | tr -d ' ')

    if [[ -z "$source" ]]; then
        log_error "Could not determine volume source from compose config"
        exit 1
    fi

    if [[ "$vol_type" == "bind" ]]; then
        VOLUME_TYPE="bind"
        BIND_MOUNT_PATH="$source"
        log_info "Detected bind mount: $BIND_MOUNT_PATH"
    else
        VOLUME_TYPE="named"
        NAMED_VOLUME_NAME="$source"
        log_info "Detected named volume: $NAMED_VOLUME_NAME"
    fi
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

    # Detect volume type for later use
    detect_volume_type
}

# Create and validate backup
create_backup() {
    log_info "Creating database backup: $BACKUP_FILE"
    log_info "This may take a while for large databases..."

    docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
        pg_dumpall -U "$POSTGRES_USER" > "$BACKUP_FILE"

    # Validate backup file
    if [[ ! -s "$BACKUP_FILE" ]]; then
        log_error "Backup file is empty! pg_dumpall may have failed."
        rm -f "$BACKUP_FILE"
        exit 1
    fi

    # Check for pg_dumpall success markers
    if ! grep -q "PostgreSQL database dump complete" "$BACKUP_FILE"; then
        log_error "Backup file appears incomplete (missing completion marker)"
        exit 1
    fi

    BACKUP_SIZE=$(du -h "$BACKUP_FILE" | cut -f1)
    log_info "Backup complete and validated: $BACKUP_FILE ($BACKUP_SIZE)"
}

# Stop all services (app will fail if postgres goes down while it's running)
stop_services() {
    log_info "Stopping all services..."
    docker compose -f "$COMPOSE_FILE" down
    STATE_SERVICES_STOPPED=true
}

# Update compose file
update_compose_file() {
    log_info "Updating compose file for PostgreSQL 18..."

    # Backup original compose file
    cp "$COMPOSE_FILE" "${COMPOSE_FILE}.backup"
    STATE_COMPOSE_BACKED_UP=true
    log_info "Original compose file backed up to: ${COMPOSE_FILE}.backup"

    # Update image version: postgres:17* -> postgres:18*
    # Handles: postgres:17, postgres:17-alpine, postgres:17.x, etc.
    sed -i.tmp 's/postgres:17-alpine/postgres:18-alpine/g' "$COMPOSE_FILE"
    sed -i.tmp 's/postgres:17\([^0-9a-z-]\)/postgres:18\1/g' "$COMPOSE_FILE"
    sed -i.tmp 's/postgres:17$/postgres:18/g' "$COMPOSE_FILE"

    # Update volume mount path: /var/lib/postgresql/data -> /var/lib/postgresql
    # PostgreSQL 18 uses version-specific subdirectories (18/data)
    sed -i.tmp 's|/var/lib/postgresql/data|/var/lib/postgresql|g' "$COMPOSE_FILE"

    # For named volumes, rename to new volume (preserves old for rollback)
    if [[ "$VOLUME_TYPE" == "named" && -n "$NAMED_VOLUME_NAME" ]]; then
        NEW_VOLUME_NAME="${NAMED_VOLUME_NAME}-pg18"
        log_info "Renaming volume: $NAMED_VOLUME_NAME -> $NEW_VOLUME_NAME"

        # Update volume reference in service (e.g., postgres-data:/var/lib/postgresql)
        sed -i.tmp "s|${NAMED_VOLUME_NAME}:/var/lib/postgresql|${NEW_VOLUME_NAME}:/var/lib/postgresql|g" "$COMPOSE_FILE"

        # Update volume definition in top-level volumes section
        # Handles both 'volume-name:' and '  volume-name:' formats
        sed -i.tmp "s|^  ${NAMED_VOLUME_NAME}:|  ${NEW_VOLUME_NAME}:|g" "$COMPOSE_FILE"
        sed -i.tmp "s|^${NAMED_VOLUME_NAME}:|${NEW_VOLUME_NAME}:|g" "$COMPOSE_FILE"
    fi

    # Clean up temp files from sed
    rm -f "${COMPOSE_FILE}.tmp"

    STATE_COMPOSE_MODIFIED=true
    log_info "Compose file updated"
}


# Prepare volumes for fresh PG18 init
prepare_volumes() {
    log_info "Preparing volumes for PostgreSQL 18..."
    log_warn "The old PostgreSQL 17 data will be preserved."

    if [[ "$VOLUME_TYPE" == "bind" ]]; then
        # For bind mounts, rename old directory (preserves data for rollback)
        if [[ -d "$BIND_MOUNT_PATH" ]]; then
            OLD_DATA_BACKUP="${BIND_MOUNT_PATH}_pg17_$(date +%Y%m%d_%H%M%S)"
            log_info "Moving old data directory to: $OLD_DATA_BACKUP"
            mv "$BIND_MOUNT_PATH" "$OLD_DATA_BACKUP"
        fi

        # Create fresh directory
        mkdir -p "$BIND_MOUNT_PATH"
        log_info "Created fresh data directory: $BIND_MOUNT_PATH"

    elif [[ "$VOLUME_TYPE" == "named" ]]; then
        # For named volumes, we use a new volume name (old volume preserved for rollback)
        # Docker Compose will create the new volume automatically on startup
        log_info "Old volume '$NAMED_VOLUME_NAME' will be preserved"
        log_info "New volume '$NEW_VOLUME_NAME' will be created on startup"
    fi

    STATE_VOLUME_MODIFIED=true
}

# Start PostgreSQL 18
start_postgres() {
    log_info "Starting PostgreSQL 18..."
    docker compose -f "$COMPOSE_FILE" up -d "$POSTGRES_SERVICE"
    STATE_PG18_STARTED=true

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
    return 1
}

# Restore backup with proper error handling
restore_backup() {
    log_info "Restoring database from backup..."
    log_info "This may take a while for large databases..."

    # Create a temp file to capture stderr
    local error_log
    error_log=$(mktemp)

    # Run restore, filtering expected warnings but capturing real errors
    # Expected warnings: "role already exists", "database already exists"
    local restore_exit_code=0
    docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
        psql -U "$POSTGRES_USER" -d postgres < "$BACKUP_FILE" 2>"$error_log" || restore_exit_code=$?

    # Check for actual errors (not just warnings)
    local real_errors
    real_errors=$(grep -v -E "(already exists|ERROR:  role .* already exists|ERROR:  database .* already exists)" "$error_log" | grep -E "^(ERROR|FATAL|PANIC)" || true)

    if [[ -n "$real_errors" ]]; then
        log_error "Restore encountered errors:"
        echo "$real_errors"
        rm -f "$error_log"
        return 1
    fi

    # Show filtered warnings for transparency
    local warnings
    warnings=$(grep -E "(already exists)" "$error_log" | head -5 || true)
    if [[ -n "$warnings" ]]; then
        log_info "Ignored expected warnings (roles/databases already exist)"
    fi

    rm -f "$error_log"
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
        return 1
    fi

    # Verify we can query the application database
    if docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
        psql -U "$POSTGRES_USER" -d telegram_groups_admin -c "SELECT COUNT(*) FROM messages;" &>/dev/null; then
        local msg_count
        msg_count=$(docker compose -f "$COMPOSE_FILE" exec -T "$POSTGRES_SERVICE" \
            psql -U "$POSTGRES_USER" -d telegram_groups_admin -t -c "SELECT COUNT(*) FROM messages;" | tr -d ' ')
        log_info "Verified application database accessible ($msg_count messages)"
    else
        log_warn "Could not verify application database (may not exist yet)"
    fi
}

# Cleanup info
cleanup_info() {
    log_info "Cleanup options:"
    echo "  - Backup file preserved: $BACKUP_FILE"
    echo "  - Original compose file: ${COMPOSE_FILE}.backup"
    echo ""
    echo "Once you've verified everything works (wait 24-48 hours), delete these files:"
    echo "  rm $BACKUP_FILE"
    echo "  rm ${COMPOSE_FILE}.backup"

    # If we moved old data directory (bind mount), mention it
    if [[ -n "${OLD_DATA_BACKUP:-}" && -d "$OLD_DATA_BACKUP" ]]; then
        echo "  rm -rf $OLD_DATA_BACKUP"
    fi

    # If we created a new volume (named volume), mention the old one can be removed
    if [[ "$VOLUME_TYPE" == "named" && -n "$NAMED_VOLUME_NAME" ]]; then
        echo ""
        echo "Old PostgreSQL 17 volume preserved for rollback:"
        echo "  docker volume rm <project>_${NAMED_VOLUME_NAME}"
        echo "  (Use 'docker volume ls' to find exact name with project prefix)"
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
    echo "  2. Stop all services (docker compose down)"
    echo "  3. Update compose.yml for PostgreSQL 18"
    echo "  4. Prepare volumes ($VOLUME_TYPE: ${BIND_MOUNT_PATH:-$NAMED_VOLUME_NAME})"
    echo "  5. Start PostgreSQL 18 and restore backup"
    echo ""
    echo "  If anything fails, changes will be rolled back automatically."
    echo "  After completion, start your services manually: docker compose up -d"
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
    stop_services
    update_compose_file
    prepare_volumes
    start_postgres
    restore_backup
    verify_upgrade

    # Disable error trap - we succeeded!
    trap - ERR

    echo ""
    echo "============================================="
    log_info "Upgrade complete!"
    echo "============================================="
    echo ""

    cleanup_info
}

# Run main function
main "$@"
