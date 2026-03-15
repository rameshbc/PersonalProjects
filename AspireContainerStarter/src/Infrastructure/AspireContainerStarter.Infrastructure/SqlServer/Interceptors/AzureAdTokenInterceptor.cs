using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AspireContainerStarter.Infrastructure.SqlServer.Interceptors;

/// <summary>
/// EF Core connection interceptor that injects an Azure AD access token
/// into every SqlConnection before it is opened. Used to authenticate
/// against Azure SQL using Managed Identity (or DefaultAzureCredential
/// in local dev via Azure CLI / Visual Studio credentials).
/// </summary>
internal sealed class AzureAdTokenInterceptor : DbConnectionInterceptor
{
    // Azure SQL resource scope — constant across all Azure environments.
    private const string AzureSqlScope = "https://database.windows.net/.default";

    private readonly TokenCredential _credential;

    public AzureAdTokenInterceptor(TokenCredential credential)
        => _credential = credential;

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        System.Data.Common.DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlConnection)
            sqlConnection.AccessToken = await AcquireTokenAsync(cancellationToken);

        return result;
    }

    public override InterceptionResult ConnectionOpening(
        System.Data.Common.DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is SqlConnection sqlConnection)
            sqlConnection.AccessToken = AcquireTokenAsync(CancellationToken.None).GetAwaiter().GetResult();

        return result;
    }

    private async Task<string> AcquireTokenAsync(CancellationToken ct)
    {
        var tokenRequest = new TokenRequestContext([AzureSqlScope]);
        var token = await _credential.GetTokenAsync(tokenRequest, ct);
        return token.Token;
    }
}
