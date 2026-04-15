// ── User-Assigned Managed Identity Module ─────────────────────────────────
// Creates a user-assigned managed identity for app-only Graph API access.
// This identity will be used by API, Worker, and Admin services to authenticate
// to Microsoft Graph without storing credentials.

param miName string
param location string

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: miName
  location: location
}

output id string = managedIdentity.id
output principalId string = managedIdentity.properties.principalId
output clientId string = managedIdentity.properties.clientId
