#!/bin/bash

# Sync PostgreSQL data and DataProtection keys from local machine to work1
# This fully replaces the remote data folder and keys with local data

set -e

LOCAL_DATA_DIR="./compose/data"
LOCAL_KEYS_DIR="./TelegramGroupsAdmin/bin/Debug/net10.0/keys"
REMOTE_HOST="kass@work1"
REMOTE_DATA_PATH="/DataPool/Repos/kass/TgSpam-PreFilterApi/compose/data"
REMOTE_KEYS_PATH="/DataPool/Repos/kass/TgSpam-PreFilterApi/TelegramGroupsAdmin/bin/Debug/net10.0/keys"

echo "🔄 Syncing PostgreSQL data and DataProtection keys from local to work1..."
echo "Local Data:  $LOCAL_DATA_DIR"
echo "Remote Data: $REMOTE_HOST:$REMOTE_DATA_PATH"
echo "Local Keys:  $LOCAL_KEYS_DIR"
echo "Remote Keys: $REMOTE_HOST:$REMOTE_KEYS_PATH"
echo ""

# Confirm action
read -p "⚠️  This will REPLACE all data on work1. Continue? (y/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "❌ Sync cancelled"
    exit 1
fi

# Stop remote containers first
echo "🛑 Stopping remote Docker containers..."
ssh $REMOTE_HOST "cd /DataPool/Repos/kass/TgSpam-PreFilterApi/compose && docker compose down"

# Take ownership of remote data folder (postgres files are owned by uid 70)
echo "🔓 Taking ownership of remote data folder (requires sudo password)..."
ssh -t $REMOTE_HOST "sudo chown -R kass:kass $REMOTE_DATA_PATH"

# Rsync local data to remote (with progress)
echo "📦 Syncing data folder..."
rsync -avz --progress "$LOCAL_DATA_DIR/" "$REMOTE_HOST:$REMOTE_DATA_PATH/"

# Ensure remote keys directory exists
echo "📁 Ensuring remote keys directory exists..."
ssh $REMOTE_HOST "mkdir -p $REMOTE_KEYS_PATH"

# Rsync local keys to remote (with progress)
echo "🔑 Syncing DataProtection keys..."
rsync -avz --progress "$LOCAL_KEYS_DIR/" "$REMOTE_HOST:$REMOTE_KEYS_PATH/"

echo ""
echo "✅ Sync complete!"
echo ""
echo "To start the remote containers:"
echo "  ssh $REMOTE_HOST 'cd /DataPool/Repos/kass/TgSpam-PreFilterApi/compose && docker compose up -d'"
