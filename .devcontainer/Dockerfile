# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.183.0/containers/dotnet/.devcontainer/base.Dockerfile

# [Choice] .NET version: 5.0, 3.1, 2.1
ARG VARIANT="5.0"

FROM mcr.microsoft.com/vscode/devcontainers/base:ubuntu

RUN wget https://dot.net/v1/dotnet-install.sh -O $HOME/dotnet-install.sh

RUN chmod +x $HOME/dotnet-install.sh

RUN sudo mkdir -p /usr/share/dotnet

RUN $HOME/dotnet-install.sh -c 5.0 --install-dir /usr/share/dotnet

RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
