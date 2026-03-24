# syntax=docker/dockerfile:1
###############################################################################
# Stage 1 — restore
###############################################################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src
COPY ["src/Messaging.Core/Messaging.Core.csproj",                   "src/Messaging.Core/"]
COPY ["samples/SamplePublisher.Api/SamplePublisher.Api.csproj",     "samples/SamplePublisher.Api/"]
RUN dotnet restore "samples/SamplePublisher.Api/SamplePublisher.Api.csproj"

###############################################################################
# Stage 2 — build & publish
###############################################################################
FROM restore AS publish
COPY . .
RUN dotnet publish "samples/SamplePublisher.Api/SamplePublisher.Api.csproj" \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

###############################################################################
# Stage 3 — runtime (ASP.NET Core, minimal, non-root)
###############################################################################
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

USER app
WORKDIR /app

COPY --from=publish --chown=app:app /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8080

# Liveness probe — ASP.NET Core responds on / with 200 after startup
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://localhost:8080/healthz 2>/dev/null || exit 1

EXPOSE 8080

ENTRYPOINT ["dotnet", "SamplePublisher.Api.dll"]
