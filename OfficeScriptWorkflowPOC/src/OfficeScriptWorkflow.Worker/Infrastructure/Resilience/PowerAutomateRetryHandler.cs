namespace OfficeScriptWorkflow.Worker.Infrastructure.Resilience;

/// <summary>
/// DelegatingHandler that can be used for any pre/post-request cross-cutting concerns
/// beyond what Polly handles (e.g. correlation IDs, request logging hooks).
/// Polly policies are applied at the IHttpClientFactory registration level, not here.
/// </summary>
public sealed class PowerAutomateRetryHandler : DelegatingHandler
{
    private readonly ILogger<PowerAutomateRetryHandler> _logger;

    public PowerAutomateRetryHandler(ILogger<PowerAutomateRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Attach a correlation ID so Power Automate runs can be correlated in logs.
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

        _logger.LogDebug(
            "Sending request. CorrelationId: {CorrelationId} Method: {Method} Uri: {Uri}",
            correlationId, request.Method, request.RequestUri?.Host);

        var response = await base.SendAsync(request, cancellationToken);

        _logger.LogDebug(
            "Received response. CorrelationId: {CorrelationId} Status: {Status}",
            correlationId, (int)response.StatusCode);

        return response;
    }
}
