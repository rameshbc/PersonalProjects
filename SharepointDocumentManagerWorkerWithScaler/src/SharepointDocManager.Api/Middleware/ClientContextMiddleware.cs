namespace SharepointDocManager.Api.Middleware;

/// <summary>
/// Reads the X-Client-Id header from every request and stores it in HttpContext.Items.
///
/// Why a header instead of a route parameter?
///   Most endpoints already have {clientId} in the route. This middleware provides
///   a fallback for endpoints that don't (e.g. webhook receivers, health checks that
///   need client context) and can be used as a cross-cutting logging enricher.
///
/// ClientId validation:
///   The middleware does NOT validate the clientId against the database on every request
///   (that would be an extra DB round-trip per call). Validation happens inside the
///   StorageAdapterResolver when the first adapter call is made.
/// </summary>
public sealed class ClientContextMiddleware
{
    public const string ClientIdKey = "ClientId";

    private readonly RequestDelegate _next;
    private readonly ILogger<ClientContextMiddleware> _logger;

    public ClientContextMiddleware(RequestDelegate next, ILogger<ClientContextMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Client-Id", out var clientId) &&
            !string.IsNullOrWhiteSpace(clientId))
        {
            context.Items[ClientIdKey] = clientId.ToString();

            // Enrich all structured logs in this request with the client ID
            using (_logger.BeginScope(new Dictionary<string, object>
                   { ["ClientId"] = clientId.ToString() }))
            {
                await _next(context);
                return;
            }
        }

        await _next(context);
    }
}

public static class ClientContextMiddlewareExtensions
{
    public static IApplicationBuilder UseClientContext(this IApplicationBuilder app)
        => app.UseMiddleware<ClientContextMiddleware>();
}
