using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ReadingTracker.Library.Entries;

/// <summary>
/// Announces that a reader moved a book to a different ReadingStatus. Carries the status it
/// came from as well as the one it went to, so a consumer can tell "started reading" from
/// "finished" without keeping its own copy of the previous state.
/// </summary>
public sealed record ReadingStatusChanged(
    Guid LibraryEntryId,
    string ReaderId,
    Guid BookId,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset ChangedAt);

public interface ILibraryEvents
{
    Task PublishAsync(ReadingStatusChanged statusChanged, CancellationToken cancellationToken);
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:55672/";

    public string Exchange { get; set; } = "readingtracker.library";
}

/// <summary>
/// Publishes Library's domain events (ADR-0003).
///
/// Best-effort, as in Catalog: the reader's status change is the outcome they asked for, and
/// failing to announce it must not fail their request. Delivery is therefore at-most-once; a
/// system needing every event would write it to an outbox in the same transaction and relay
/// it separately.
/// </summary>
public sealed class RabbitMqLibraryEvents(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqLibraryEvents> logger) : ILibraryEvents, IAsyncDisposable
{
    public const string RoutingKey = "reading-status.changed";

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connecting = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(ReadingStatusChanged statusChanged, CancellationToken cancellationToken)
    {
        try
        {
            var channel = await OpenChannelAsync(cancellationToken);

            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: RoutingKey,
                body: JsonSerializer.SerializeToUtf8Bytes(statusChanged),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not announce the status change on entry {LibraryEntryId}; the change itself was saved",
                statusChanged.LibraryEntryId);
        }
    }

    private async Task<IChannel> OpenChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _connecting.WaitAsync(cancellationToken);

        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            var factory = new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _connecting.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connecting.Dispose();
    }
}
