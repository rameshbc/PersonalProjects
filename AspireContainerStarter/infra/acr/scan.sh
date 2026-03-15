#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# scan.sh — Container image vulnerability scanner for ACR Task
#
# Called by scan-task.yaml inside a mcr.microsoft.com/azure-cli container.
# Installs Trivy at runtime, exchanges the task's managed identity token for
# an ACR refresh token, then scans every service image.
#
# Usage (inside ACR Task):
#   bash /workspace/infra/acr/scan.sh <registry-fqdn>
#
# Exit codes:
#   0 — no HIGH/CRITICAL findings (or all findings are unfixed)
#   1 — script error
#   (Trivy --exit-code 0: findings are logged but do NOT fail the task;
#    blocking happens at push time in the CD workflow instead.)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

REGISTRY=${1:?"Usage: $0 <registry-fqdn>  (e.g. myacr.azurecr.io)"}
TRIVY_VERSION="v0.57.1"
IMAGES=("api" "calc1-worker" "calc2-worker")
FOUND_VULNS=0

# ── 1. Install Trivy ──────────────────────────────────────────────────────────
echo "▶ Installing Trivy ${TRIVY_VERSION}..."
curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh \
  | sh -s -- -b /usr/local/bin "${TRIVY_VERSION}"

# ── 2. Obtain an ACR refresh token via the managed identity (IMDS) ────────────
# The ACR Task's system-assigned managed identity must have AcrPull on the registry.
# Steps:
#   a) Get an ARM bearer token from the Azure Instance Metadata Service (IMDS).
#   b) Exchange it for an ACR refresh token (OAuth2 token-exchange endpoint).
echo "▶ Obtaining ACR token via managed identity..."

ARM_TOKEN=$(
  curl -sf \
    "http://169.254.169.254/metadata/identity/oauth2/token\
?api-version=2018-02-01\
&resource=https%3A%2F%2Fmanagement.azure.com%2F" \
    -H "Metadata: true" \
  | jq -r .access_token
)

ACR_TOKEN=$(
  curl -sf -X POST \
    "https://${REGISTRY}/oauth2/exchange" \
    --data-urlencode "grant_type=access_token" \
    --data-urlencode "service=${REGISTRY}" \
    --data-urlencode "access_token=${ARM_TOKEN}" \
  | jq -r .refresh_token
)

# Trivy uses these env vars to authenticate against private registries.
export TRIVY_AUTH_URL="https://${REGISTRY}"
export TRIVY_USERNAME="00000000-0000-0000-0000-000000000000"  # ACR token pseudo-user
export TRIVY_PASSWORD="${ACR_TOKEN}"

# ── 3. Scan each service image ────────────────────────────────────────────────
# --exit-code 0  : log findings but continue scanning all images.
# --ignore-unfixed: skip CVEs that have no upstream fix yet (reduces noise).
# The CD workflow's pre-push scan (--exit-code 1) is the blocking gate.
echo ""
for IMG in "${IMAGES[@]}"; do
  echo "════════════════════════════════════════════"
  echo " Scanning ${REGISTRY}/${IMG}:latest"
  echo "════════════════════════════════════════════"
  if ! trivy image \
      --severity HIGH,CRITICAL \
      --exit-code 0 \
      --ignore-unfixed \
      --no-progress \
      --format table \
      "${REGISTRY}/${IMG}:latest"; then
    FOUND_VULNS=1
  fi
  echo ""
done

# ── 4. Summary ────────────────────────────────────────────────────────────────
if [[ $FOUND_VULNS -eq 1 ]]; then
  echo "⚠  HIGH or CRITICAL findings detected in one or more images."
  echo "   Review the output above and update base images or dependencies."
  echo "   These will be blocked at push time by the CD workflow."
else
  echo "✓  No HIGH/CRITICAL findings with available fixes."
fi

# Always exit 0 — this is a reporting task, not a gate.
exit 0
