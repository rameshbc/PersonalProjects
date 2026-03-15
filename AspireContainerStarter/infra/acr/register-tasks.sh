#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# register-tasks.sh — One-time setup for ACR vulnerability scanning
#
# What this script does:
#   1. Enables Microsoft Defender for Containers (continuous background scan —
#      fires on every ACR push, no configuration needed afterward).
#   2. Creates an ACR multi-step Task that runs Trivy daily at 02:00 UTC
#      (covers images that weren't recently pushed and thus weren't scanned
#       by Defender's push trigger).
#   3. Grants the task's managed identity AcrPull on the registry.
#   4. (Optional) Assigns RBAC roles to Container App managed identities for
#      ACR pull and Service Bus consume — paste the principal IDs from the
#      Bicep deployment output.
#
# Usage:
#   ./infra/acr/register-tasks.sh \
#     <ACR_NAME>        \   # short name, e.g. aspireprodacr
#     <RESOURCE_GROUP>  \   # resource group containing the ACR
#     <GH_PAT>          \   # GitHub PAT with repo:read scope (for task git context)
#     <GH_ORG/REPO>         # e.g. myorg/AspireContainerStarter
#
# Prerequisites:
#   - az CLI logged in with Owner/Contributor + Security Admin on the subscription
#   - The ACR already exists
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

ACR_NAME=${1:?"ACR short name required (e.g. aspireprodacr)"}
RESOURCE_GROUP=${2:?"Resource group required"}
GH_PAT=${3:?"GitHub PAT required (repo:read scope)"}
GH_REPO=${4:?"GitHub org/repo required (e.g. myorg/AspireContainerStarter)"}

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
ACR_ID=$(az acr show --name "$ACR_NAME" --resource-group "$RESOURCE_GROUP" --query id -o tsv)
TASK_NAME="vulnerability-scan"

echo "═══════════════════════════════════════════════════════════"
echo " ACR: $ACR_NAME  |  RG: $RESOURCE_GROUP"
echo " Subscription: $SUBSCRIPTION_ID"
echo "═══════════════════════════════════════════════════════════"
echo ""

# ── 1. Enable Microsoft Defender for Containers ───────────────────────────────
# This is the PRIMARY scanning mechanism.
# Every image pushed to ACR is automatically scanned; findings appear in
# Microsoft Defender for Cloud → Container image recommendations.
echo "▶ Enabling Microsoft Defender for Containers..."
az security pricing create \
  --name Containers \
  --tier Standard \
  --subscription "$SUBSCRIPTION_ID"
echo "  ✓ Defender for Containers enabled."
echo ""

# ── 2. Create ACR Task (daily Trivy scan — secondary/complementary) ───────────
echo "▶ Creating ACR Task '${TASK_NAME}' (daily at 02:00 UTC)..."
az acr task create \
  --name           "$TASK_NAME" \
  --registry       "$ACR_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --file           "infra/acr/scan-task.yaml" \
  --context        "https://github.com/${GH_REPO}.git#main" \
  --git-access-token "$GH_PAT" \
  --schedule       "0 2 * * *" \
  --assign-identity \
  --timeout        3600
echo "  ✓ Task created."
echo ""

# ── 3. Grant the task's managed identity AcrPull ──────────────────────────────
echo "▶ Granting AcrPull to task managed identity..."
TASK_PRINCIPAL_ID=$(
  az acr task show \
    --name     "$TASK_NAME" \
    --registry "$ACR_NAME" \
    --query    identity.principalId \
    -o tsv
)
az role assignment create \
  --assignee "$TASK_PRINCIPAL_ID" \
  --role     "AcrPull" \
  --scope    "$ACR_ID"
echo "  ✓ AcrPull granted to task principal: $TASK_PRINCIPAL_ID"
echo ""

# ── 4. Post-Bicep RBAC — Container App managed identities ─────────────────────
# After running the Bicep deployment, paste the output principal IDs here and
# re-run this section (comment out steps 1-3 above first).
#
# Required roles per service:
#   API          → AcrPull (on ACR)
#   Calc1Worker  → AcrPull (on ACR) + Azure Service Bus Data Receiver (on namespace)
#   Calc2Worker  → AcrPull (on ACR) + Azure Service Bus Data Receiver (on namespace)
#
# Example:
#
#   API_PRINCIPAL_ID="<from Bicep output: apiPrincipalId>"
#   CALC1_PRINCIPAL_ID="<from Bicep output: calc1WorkerPrincipalId>"
#   CALC2_PRINCIPAL_ID="<from Bicep output: calc2WorkerPrincipalId>"
#   SB_NAMESPACE_ID="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.ServiceBus/namespaces/<sb-name>"
#
#   for PRINCIPAL in "$API_PRINCIPAL_ID" "$CALC1_PRINCIPAL_ID" "$CALC2_PRINCIPAL_ID"; do
#     az role assignment create --assignee "$PRINCIPAL" --role "AcrPull" --scope "$ACR_ID"
#   done
#
#   for PRINCIPAL in "$CALC1_PRINCIPAL_ID" "$CALC2_PRINCIPAL_ID"; do
#     az role assignment create \
#       --assignee "$PRINCIPAL" \
#       --role "Azure Service Bus Data Receiver" \
#       --scope "$SB_NAMESPACE_ID"
#   done

echo "═══════════════════════════════════════════════════════════"
echo " Setup complete."
echo ""
echo " Defender for Containers scans every push to ACR automatically."
echo " ACR Task '$TASK_NAME' runs daily at 02:00 UTC."
echo ""
echo " Trigger a manual scan run:"
echo "   az acr task run --name $TASK_NAME --registry $ACR_NAME"
echo ""
echo " View task run logs:"
echo "   az acr task logs --name $TASK_NAME --registry $ACR_NAME"
echo "═══════════════════════════════════════════════════════════"
