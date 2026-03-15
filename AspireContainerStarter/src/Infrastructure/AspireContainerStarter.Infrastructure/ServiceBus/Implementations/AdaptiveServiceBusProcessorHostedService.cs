using System.Diagnostics.Metrics;
using System.Text.Json;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using AspireContainerStarter.Infrastructure.ServiceBus.Monitoring;
using AspireContainerStarter.Infrastructure.ServiceBus.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireContainerStarter.Infrastructure.ServiceBus.Implementations;

/// <summary>
/// Adaptive variant of <see cref="ServiceBusProcessorHostedService{TMessage}"/> that
/// dynamically adjusts in-process message concurrency based on queue depth growth rate.
///
/// Two-tier scaling:
///   1. KEDA (instance-level) — scales Container App replicas 0→N when queue grows.
///   2. This service (in-process) — scales concurrent handlers per instance between
///      <see cref="AdaptiveConcurrencyOptions.MinConcurrency"/> and
///      <see cref="AdaptiveConcurrencyOptions.MaxConcurrency"/>.
///
/// Concurrency control mechanism:
///   - <see cref="ServiceBusProcessor"/> is started with MaxConcurrentCalls = MaxConcurrency
///     so the broker can deliver up to MaxConcurrency messages simultaneously.
///   - A <see cref="SemaphoreSlim"/> (_gate) limits effective concurrency. Each handler
///     acquires the gate before doing real work and releases it afterward (or drains it
///     during scale-down by not releasing).
///   - A background monitor loop polls queue depth every MonitorIntervalSeconds seconds,
///     calculates the growth rate, and adjusts the target concurrency.
/// </summary>
public sealed class AdaptiveServiceBusProcessorHostedService<TMessage> : IHostedService, IAsyncDisposable
    where TMessage : class
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QueueDepthMonitor _monitor;
    private readonly AdaptiveConcurrencyOptions _options;
    private readonly ILogger<AdaptiveServiceBusProcessorHostedService<TMessage>> _logger;

    // Semaphore that gates concurrent message handlers.
    // CurrentCount = number of additional handlers that may start immediately.
    private readonly SemaphoreSlim _gate;

    // Target concurrency tracked independently of the semaphore's CurrentCount
    // so the monitor can adjust it without requiring a blocking Wait.
    private int _targetConcurrency;

    // Tracks handlers currently executing (between WaitAsync and Release).
    private int _inflight;

    // Last known queue depth used by the OTel gauge callback.
    private long _lastKnownDepth;

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Meter _meter =
        new("AspireContainerStarter.Infrastructure.ServiceBus", "1.0.0");

    public AdaptiveServiceBusProcessorHostedService(
        ServiceBusProcessor processor,
        IServiceScopeFactory scopeFactory,
        QueueDepthMonitor monitor,
        IOptions<AdaptiveConcurrencyOptions> options,
        ILogger<AdaptiveServiceBusProcessorHostedService<TMessage>> logger)
    {
        _processor  = processor;
        _scopeFactory = scopeFactory;
        _monitor    = monitor;
        _options    = options.Value;
        _logger     = logger;

        _targetConcurrency = _options.MinConcurrency;
        _gate = new SemaphoreSlim(_options.MinConcurrency, _options.MaxConcurrency);

        // OTel observable gauge — picked up by Aspire dashboard and KEDA metrics adapter.
        _meter.CreateObservableGauge(
            name: "queue.depth",
            observeValue: () => Volatile.Read(ref _lastKnownDepth),
            unit: "messages",
            description: "Current active message count in the Service Bus queue");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync   += HandleErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);

        _monitorCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = RunMonitorLoopAsync(_monitorCts.Token);

        _logger.LogInformation(
            "Adaptive processor started for {MessageType} (concurrency: {Min}–{Max}, interval: {Interval}s)",
            typeof(TMessage).Name, _options.MinConcurrency, _options.MaxConcurrency,
            _options.MonitorIntervalSeconds);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_monitorCts is not null)
        {
            await _monitorCts.CancelAsync();
            if (_monitorTask is not null)
            {
                try { await _monitorTask.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { /* expected on shutdown */ }
            }
        }

        await _processor.StopProcessingAsync(cancellationToken);
        _logger.LogInformation("Adaptive processor stopped for {MessageType}", typeof(TMessage).Name);
    }

    // ── Message handler ───────────────────────────────────────────────────

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        // Gate: block until concurrency budget allows this handler to proceed.
        await _gate.WaitAsync(args.CancellationToken);
        Interlocked.Increment(ref _inflight);

        try
        {
            var messageId     = args.Message.MessageId;
            var correlationId = args.Message.CorrelationId;

            var payload = args.Message.Body.ToObjectFromJson<TMessage>(_jsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialise {typeof(TMessage).Name}");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var consumer = scope.ServiceProvider.GetRequiredService<IMessageConsumer<TMessage>>();
            await consumer.HandleAsync(payload, messageId, correlationId, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            _logger.LogDebug("Completed message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message of type {Type}", typeof(TMessage).Name);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
        finally
        {
            int remaining = Interlocked.Decrement(ref _inflight);

            // Decide whether to release the gate permit or drain it (scale-down).
            // After we finished: capacity = _gate.CurrentCount + remaining
            // We should release if capacity < _targetConcurrency (keeps capacity at target).
            // We should drain if capacity >= _targetConcurrency (removes excess capacity).
            int target = Volatile.Read(ref _targetConcurrency);
            if (_gate.CurrentCount + remaining < target)
            {
                _gate.Release();
            }
            // else: drain — permit is absorbed, reducing effective concurrency by 1
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus error on {EntityPath}: {ErrorSource}",
            args.EntityPath, args.ErrorSource);
        return Task.CompletedTask;
    }

    // ── Monitor loop ──────────────────────────────────────────────────────

    private async Task RunMonitorLoopAsync(CancellationToken ct)
    {
        long previousDepth = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.MonitorIntervalSeconds), ct);

                var currentDepth = await _monitor.GetActiveMessageCountAsync(
                    _options.QueueName, ct);

                Volatile.Write(ref _lastKnownDepth, currentDepth);

                double growthRate = (currentDepth - previousDepth) /
                                    (double)_options.MonitorIntervalSeconds;

                _logger.LogDebug(
                    "Queue {Queue}: depth={Depth}, growthRate={Rate:F1} msgs/s, concurrency={Concurrency}",
                    _options.QueueName, currentDepth, growthRate, _targetConcurrency);

                if (growthRate > _options.GrowthThreshold)
                {
                    TryScaleUp();
                }
                else if (growthRate <= 0 && _targetConcurrency > _options.MinConcurrency)
                {
                    TryScaleDown();
                }

                previousDepth = currentDepth;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Monitor loop error for queue {Queue} — continuing", _options.QueueName);
            }
        }
    }

    private void TryScaleUp()
    {
        int newTarget = Interlocked.Increment(ref _targetConcurrency);
        if (newTarget > _options.MaxConcurrency)
        {
            Interlocked.Decrement(ref _targetConcurrency);   // cap at max
            return;
        }

        // Release one permit immediately so a waiting handler can proceed.
        _gate.Release();
        _logger.LogInformation(
            "Concurrency scaled up to {Concurrency} for {MessageType}",
            newTarget, typeof(TMessage).Name);
    }

    private void TryScaleDown()
    {
        int newTarget = Interlocked.Decrement(ref _targetConcurrency);
        if (newTarget < _options.MinConcurrency)
        {
            Interlocked.Increment(ref _targetConcurrency);   // floor at min
            return;
        }

        // No immediate gate manipulation needed — the next handler to finish
        // will detect capacity > target and drain one permit.
        _logger.LogInformation(
            "Concurrency scaled down to {Concurrency} for {MessageType}",
            newTarget, typeof(TMessage).Name);
    }

    public async ValueTask DisposeAsync()
    {
        _monitorCts?.Dispose();
        await _processor.DisposeAsync();
        _gate.Dispose();
    }
}
