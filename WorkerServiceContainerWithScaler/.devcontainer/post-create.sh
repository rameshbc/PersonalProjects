#!/usr/bin/env bash
set -euo pipefail

echo "==> Restoring .NET packages..."
dotnet restore MessagingLibrary.sln

# Wait for SQL Server to be ready before seeding user-secrets
# Use the Docker service name — on macOS "localhost" is the container itself, not sqlserver
echo "==> Waiting for SQL Server on sqlserver:1433..."
for i in $(seq 1 30); do
  if /opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -P "${SA_PASSWORD:-}" -Q "SELECT 1" -C -No &>/dev/null \
  || /opt/mssql-tools/bin/sqlcmd  -S sqlserver -U sa -P "${SA_PASSWORD:-}" -Q "SELECT 1"      &>/dev/null; then
    echo "SQL Server is up."
    break
  fi
  echo "  Waiting... ($i/30)"
  sleep 5
done

# Seed user-secrets for each sample project so developers don't have to type connection strings
# Use Docker service names — apps run inside the devcontainer on macOS, not on the host
EMULATOR_CS="Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE=;UseDevelopmentEmulator=true;"
AUDIT_CS="Server=sqlserver,1433;Database=MessagingAuditDev;User Id=sa;Password=${SA_PASSWORD:-YourStr0ng!DevPass};TrustServerCertificate=true;"

for proj in \
  samples/SampleWorker.Queue/SampleWorker.Queue.csproj \
  samples/SampleWorker.Topic/SampleWorker.Topic.csproj \
  samples/SamplePublisher.Api/SamplePublisher.Api.csproj; do

  echo "==> Setting user-secrets for $proj..."
  dotnet user-secrets set "Messaging:ConnectionString" "$EMULATOR_CS"  --project "$proj"
  dotnet user-secrets set "ConnectionStrings:AuditDb"  "$AUDIT_CS"     --project "$proj"
done

echo "==> Dev environment ready."
echo "    Start infra:  docker-compose up -d sqlserver servicebus-emulator db-init"
echo "    Run a sample: cd samples/SampleWorker.Queue && dotnet run"
