# syntax=docker/dockerfile:1
###############################################################################
# Stage 1 — restore
###############################################################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src
COPY ["src/Messaging.Core/Messaging.Core.csproj",                   "src/Messaging.Core/"]
COPY ["src/WorkerHost.Core/WorkerHost.Core.csproj",                 "src/WorkerHost.Core/"]
COPY ["samples/SampleWorker.Topic/SampleWorker.Topic.csproj",       "samples/SampleWorker.Topic/"]
RUN dotnet restore "samples/SampleWorker.Topic/SampleWorker.Topic.csproj"

###############################################################################
# Stage 2 — build & publish
###############################################################################
FROM restore AS publish
COPY . .
RUN dotnet publish "samples/SampleWorker.Topic/SampleWorker.Topic.csproj" \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

###############################################################################
# Stage 3 — runtime (minimal, non-root)
###############################################################################
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final

USER app
WORKDIR /app

COPY --from=publish --chown=app:app /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "SampleWorker.Topic.dll"]
