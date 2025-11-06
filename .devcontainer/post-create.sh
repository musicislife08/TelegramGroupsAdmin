#!/bin/bash
set -e

echo "🔧 Running post-create setup..."

# Install Claude Code CLI globally
echo "📦 Installing Claude Code CLI..."
npm install -g @anthropic-ai/claude-code

# Install EF Core tools if not already installed
if ! dotnet tool list --global | grep -q dotnet-ef; then
    echo "📦 Installing EF Core tools..."
    dotnet tool install --global dotnet-ef
fi

# Restore NuGet packages
echo "📦 Restoring NuGet packages..."
dotnet restore

# Wait for PostgreSQL to be fully ready
echo "⏳ Waiting for PostgreSQL to be ready..."
until pg_isready -h localhost -p 5432 -U tgadmin; do
  echo "PostgreSQL is unavailable - sleeping"
  sleep 2
done

echo "✅ PostgreSQL is ready!"

# Run database migrations
echo "🗄️  Running database migrations..."
cd TelegramGroupsAdmin.Data
dotnet ef database update --startup-project ../TelegramGroupsAdmin
cd ..

echo "✅ Database migrations applied!"

# Build the solution to verify everything works
echo "🔨 Building solution..."
dotnet build --no-restore

echo "✅ Post-create setup complete!"
echo ""
echo "🚀 Ready to code! Press F5 to start debugging."
echo "📝 Connection string: Host=localhost;Port=5432;Database=telegram_groups_admin;Username=tgadmin;Password=devpassword"
