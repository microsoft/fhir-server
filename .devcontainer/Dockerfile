# Use modern DevContainers .NET image with pre-installed .NET SDKs
# This eliminates the need to download external scripts and provides a stable foundation
FROM mcr.microsoft.com/devcontainers/dotnet:9.0

# Install additional packages needed for FHIR development
RUN apt-get update && apt-get install -y \
    dos2unix \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Install .NET 6 SDK for compatibility (project uses both .NET 6 and 9)
RUN curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
    && echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/12/prod bookworm main" > /etc/apt/sources.list.d/microsoft-prod.list \
    && apt-get update \
    && apt-get install -y dotnet-sdk-6.0 \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# The modern DevContainers base image handles Docker access automatically
# No custom entrypoint needed for basic functionality

EXPOSE 44348