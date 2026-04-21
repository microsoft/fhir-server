FROM mcr.microsoft.com/devcontainers/base:bookworm

# [Option] Install zsh
ARG INSTALL_ZSH="true"
# [Option] Upgrade OS packages to their latest versions
ARG UPGRADE_PACKAGES="false"
# Install needed packages and setup non-root user. Use a separate RUN statement to add your
# own dependencies. A user of "automatic" attempts to reuse an user ID if one already exists.
#ARG USERNAME=
ARG USERNAME=automatic
ARG USER_UID=1000
ARG USER_GID=$USER_UID

RUN sudo mkdir -p /setup
COPY .devcontainer/library-scripts/*.sh /setup/library-scripts/
COPY .devcontainer/library-scripts/setup.sh /setup/setup.sh
RUN wget https://dot.net/v1/dotnet-install.sh -O /setup/dotnet-install.sh
RUN chmod +x /setup/dotnet-install.sh

RUN apt-get update \
    && apt-get install -y dos2unix openssl \
    && dos2unix /setup/library-scripts/*.sh \
    # Clean up
    && apt-get autoremove -y && apt-get clean -y && rm -rf /var/lib/apt/lists/* /setup/library-scripts/

# Install .net SDK
COPY global.json /setup/global.json
COPY build/dotnet8-compat/global.json /setup/global-dotnet8-compat.json
RUN dos2unix /setup/*.sh
RUN chmod +x /setup/*.sh
RUN sudo mkdir -p /usr/share/dotnet
RUN /setup/dotnet-install.sh --install-dir /usr/share/dotnet --jsonfile /setup/global-dotnet8-compat.json
RUN /setup/dotnet-install.sh --install-dir /usr/share/dotnet --jsonfile /setup/global.json
# Keep runtime patches current so build-time generators can run even when package
# runtime requirements are newer than the SDK-bundled runtimes.
RUN /setup/dotnet-install.sh --install-dir /usr/share/dotnet --runtime dotnet --channel 8.0 --quality ga
RUN /setup/dotnet-install.sh --install-dir /usr/share/dotnet --runtime dotnet --channel 9.0 --quality ga
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# [Optional] Uncomment this section to install additional OS packages.
# RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
#     && apt-get -y install --no-install-recommends <your-package-list-here>

EXPOSE 44348
