# Base image with .NET 9 SDK
FROM mcr.microsoft.com/devcontainers/base:ubuntu

# Install additional tools
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    postgresql-client \
    jq \
    tree \
    htop \
    vim \
    tmux \
    unzip \
    && apt-get clean -y && rm -rf /var/lib/apt/lists/*

# Install yq (YAML processor) - detect architecture for multi-platform support
RUN ARCH=$(dpkg --print-architecture) && \
    if [ "$ARCH" = "amd64" ]; then YQ_ARCH="amd64"; \
    elif [ "$ARCH" = "arm64" ]; then YQ_ARCH="arm64"; \
    else echo "Unsupported architecture: $ARCH" && exit 1; fi && \
    wget -qO /usr/local/bin/yq "https://github.com/mikefarah/yq/releases/latest/download/yq_linux_${YQ_ARCH}" && \
    chmod +x /usr/local/bin/yq

# Note: dotnet-ef installed in post-create.sh after .NET feature is applied
# Add dotnet tools to PATH for when they're installed
ENV PATH="${PATH}:/root/.dotnet/tools:/home/vscode/.dotnet/tools"

# Set working directory
WORKDIR /workspace
