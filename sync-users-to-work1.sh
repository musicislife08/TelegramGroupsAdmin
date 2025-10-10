#!/bin/bash
set -e

# Script to export user data from local machine and import to work1 machine
# This handles DataProtection key differences by decrypting on source and re-encrypting on destination

REMOTE_HOST="kass@work1"
REMOTE_PATH="/DataPool/Repos/kass/TgSpam-PreFilterApi"
LOCAL_TEMP="/tmp/users_export.json"
REMOTE_TEMP="/tmp/users_export.json"

# Trap to ensure cleanup on exit
cleanup() {
    echo ""
    echo "Cleaning up temporary files..."
    rm -f "$LOCAL_TEMP"
    ssh "$REMOTE_HOST" "rm -f $REMOTE_TEMP" 2>/dev/null || true
    echo "✓ Cleanup complete"
}
trap cleanup EXIT

echo "========================================="
echo "User Data Export/Import to work1"
echo "========================================="
echo ""

# Remove any existing temp files first
echo "Removing any existing temp files..."
rm -f "$LOCAL_TEMP"
ssh "$REMOTE_HOST" "rm -f $REMOTE_TEMP" 2>/dev/null || true
echo ""

# Step 1: Export user data locally to /tmp (decrypted)
echo "Step 1: Exporting user data locally to temp file..."
dotnet run --project TelegramGroupsAdmin/TelegramGroupsAdmin.csproj -- --export-users "$LOCAL_TEMP"

if [ ! -f "$LOCAL_TEMP" ]; then
    echo "ERROR: Export file not created at $LOCAL_TEMP"
    exit 1
fi

echo "✓ Export complete: $LOCAL_TEMP"
echo ""

# Step 2: Copy export file to work1's /tmp
echo "Step 2: Copying export file to work1:/tmp..."
scp "$LOCAL_TEMP" "$REMOTE_HOST:$REMOTE_TEMP"
echo "✓ File copied to work1"
echo ""

# Step 3: Import on work1 (will re-encrypt with work1's keys)
echo "Step 3: Importing user data on work1..."
ssh "$REMOTE_HOST" "cd $REMOTE_PATH && /home/kass/.dotnet/dotnet run --project TelegramGroupsAdmin/TelegramGroupsAdmin.csproj -- --import-users $REMOTE_TEMP"
echo "✓ Import complete on work1"
echo ""

echo "========================================="
echo "✓ User data migration complete!"
echo "========================================="
