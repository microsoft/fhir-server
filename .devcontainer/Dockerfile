FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV PATH="${DOTNET_ROOT}:${PATH}"

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates curl libicu74 libkrb5-3 libssl3t64 openssl zlib1g \
    && curl --fail --silent --show-error --location \
        https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
        --output /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && ACCEPT_EULA=Y apt-get install -y --no-install-recommends mssql-tools18 \
    && rm -rf /var/lib/apt/lists/*

COPY global.json /setup/global.json
COPY build/dotnet10-compat/global.json /setup/dotnet10-compat/global.json
COPY .devcontainer/*.sh /setup/

RUN curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh --output /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --install-dir "${DOTNET_ROOT}" --jsonfile /setup/dotnet10-compat/global.json --no-path \
    && bash /tmp/dotnet-install.sh --install-dir "${DOTNET_ROOT}" --jsonfile /setup/global.json --no-path \
    && rm /tmp/dotnet-install.sh \
    && sed -i 's/\r$//' /setup/*.sh \
    && chmod +x /setup/*.sh
