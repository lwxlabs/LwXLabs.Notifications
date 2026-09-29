using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Flow.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class NotificationOutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    public void Wake() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan maximumWait, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumWait);
        try { await _channel.Reader.ReadAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }
}

internal sealed class NotificationOutboxService : BackgroundService
{
    private readonly OutboxDispatcher _dispatcher;
    private readonly NotificationOutboxSignal _signal;
    private readonly OutboxDispatcherOptions _options;
    private readonly ILogger<NotificationOutboxService> _logger;

    public NotificationOutboxService(OutboxDispatcher dispatcher, NotificationOutboxSignal signal,
        IOptions<OutboxDispatcherOptions> options, ILogger<NotificationOutboxService> logger)
    {
        _dispatcher = dispatcher;
        _signal = signal;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await _dispatcher.DispatchBatch(stoppingToken).ConfigureAwait(false) > 0) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Notification outbox poll failed; retrying after the poll interval."); }

            await _signal.WaitAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
