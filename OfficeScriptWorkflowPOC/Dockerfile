# ── Stage 1: build ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer-caches unless .csproj changes)
COPY src/OfficeScriptWorkflow.Worker/OfficeScriptWorkflow.Worker.csproj ./
RUN dotnet restore

# Copy source and publish
COPY src/OfficeScriptWorkflow.Worker/ ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Stage 2: runtime ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for production security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

# Expose health-check port (optional — add ASP.NET health endpoint if desired)
# EXPOSE 8080

ENTRYPOINT ["dotnet", "OfficeScriptWorkflow.Worker.dll"]
