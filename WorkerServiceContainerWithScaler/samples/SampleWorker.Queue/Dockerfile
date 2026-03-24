# syntax=docker/dockerfile:1
###############################################################################
# Stage 1 — restore (layer-cached on csproj changes only)
###############################################################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src
COPY ["src/Messaging.Core/Messaging.Core.csproj",                   "src/Messaging.Core/"]
COPY ["src/WorkerHost.Core/WorkerHost.Core.csproj",                 "src/WorkerHost.Core/"]
COPY ["samples/SampleWorker.Queue/SampleWorker.Queue.csproj",       "samples/SampleWorker.Queue/"]
RUN dotnet restore "samples/SampleWorker.Queue/SampleWorker.Queue.csproj"

###############################################################################
# Stage 2 — build & publish
###############################################################################
FROM restore AS publish
COPY . .
RUN dotnet publish "samples/SampleWorker.Queue/SampleWorker.Queue.csproj" \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

###############################################################################
# Stage 3 — runtime (minimal, non-root)
###############################################################################
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final

# Run as non-root user (created by base image in .NET 8+)
USER app
WORKDIR /app

COPY --from=publish --chown=app:app /app/publish .

# Secrets come from environment variables / mounted secrets — never baked in
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "SampleWorker.Queue.dll"]
