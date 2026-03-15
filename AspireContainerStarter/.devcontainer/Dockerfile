FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0-noble

# Remove the Yarn APT repository whose GPG key is expired/unavailable,
# which causes `apt-get update` to fail during feature installation.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && rm -f /etc/apt/keyrings/yarn.gpg \
    && apt-get update

# Configure Docker (docker-in-docker) to store images in /tmp, which is a
# tmpfs with ~4 GB free. The root overlay filesystem is only ~59 GB and fills
# quickly with the .NET SDK + NuGet packages + Docker images together.
RUN mkdir -p /etc/docker \
    && echo '{"data-root":"/tmp/docker"}' > /etc/docker/daemon.json
