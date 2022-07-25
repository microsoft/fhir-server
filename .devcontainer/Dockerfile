# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.183.0/containers/dotnet/.devcontainer/base.Dockerfile

# Note: You can use any Debian/Ubuntu based image you want. 
FROM mcr.microsoft.com/vscode/devcontainers/base:0-buster

# [Option] Install zsh
ARG INSTALL_ZSH="true"
# [Option] Upgrade OS packages to their latest versions
ARG UPGRADE_PACKAGES="false"
# [Option] Enable non-root Docker access in container
ARG ENABLE_NONROOT_DOCKER="true"
# [Option] Use the OSS Moby CLI instead of the licensed Docker CLI
ARG USE_MOBY="true"

# Install needed packages and setup non-root user. Use a separate RUN statement to add your
# own dependencies. A user of "automatic" attempts to reuse an user ID if one already exists.
ARG USERNAME=automatic
ARG USER_UID=1000
ARG USER_GID=$USER_UID

RUN sudo mkdir -p /setup
COPY .devcontainer/library-scripts/*.sh /setup/library-scripts/
COPY .devcontainer/library-scripts/fix-cert.sh /setup/fix-cert.sh
COPY global.json /setup/global.json

RUN apt-get update \
    && apt-get install -y dos2unix \
    && dos2unix /setup/library-scripts/*.sh \
    && /bin/bash /setup/library-scripts/common-debian.sh "${INSTALL_ZSH}" "${USERNAME}" "${USER_UID}" "${USER_GID}" "${UPGRADE_PACKAGES}" "true" "true" \
    # Use Docker script from script library to set things up
    && /bin/bash /setup/library-scripts/docker-debian.sh "${ENABLE_NONROOT_DOCKER}" "/var/run/docker-host.sock" "/var/run/docker.sock" "${USERNAME}" \
    # Clean up
    && apt-get autoremove -y && apt-get clean -y && rm -rf /var/lib/apt/lists/* /setup/library-scripts/


# Install .net SDK
RUN wget https://dot.net/v1/dotnet-install.sh -O $HOME/dotnet-install.sh
RUN chmod +x $HOME/dotnet-install.sh
RUN dos2unix /setup/global.json
RUN dos2unix /setup/*.sh
RUN chmod +x /setup/*.sh
RUN sudo mkdir -p /usr/share/dotnet
RUN $HOME/dotnet-install.sh --install-dir /usr/share/dotnet --jsonfile /setup/global.json
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# Setting the ENTRYPOINT to docker-init.sh will configure non-root access 
# to the Docker socket. The script will also execute CMD as needed.
ENTRYPOINT [ "/usr/local/share/docker-init.sh" ]

# [Optional] Uncomment this section to install additional OS packages.
# RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
#     && apt-get -y install --no-install-recommends <your-package-list-here>

EXPOSE 44348