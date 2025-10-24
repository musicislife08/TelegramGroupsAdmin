#!/bin/bash
set -e

REGISTRY="172.26.1.3:5000"
IMAGE_NAME="telegramgroupsadmin"
TAG="latest"

# Create and use buildx builder if it doesn't exist
if ! docker buildx inspect multiarch-builder &>/dev/null; then
    echo "Creating multiarch builder..."
    docker buildx create --name multiarch-builder --use
else
    echo "Using existing multiarch builder..."
    docker buildx use multiarch-builder
fi

echo "Building multi-arch image (linux/amd64, linux/arm64)..."
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t ${REGISTRY}/${IMAGE_NAME}:${TAG} \
  -f TelegramGroupsAdmin/Dockerfile \
  --push \
  .

echo ""
echo "✅ Multi-arch image pushed successfully!"
echo "   - linux/amd64 (x86_64 servers)"
echo "   - linux/arm64 (ARM servers, your Mac)"
echo ""
echo "View in UI: http://172.26.1.3:5001/"
echo ""
echo "To pull on any machine:"
echo "docker pull ${REGISTRY}/${IMAGE_NAME}:${TAG}"
