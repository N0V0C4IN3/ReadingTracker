using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ReadingTracker.Catalog.Books;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:55672/";

    public string Exchange { get; set; } = "readingtracker.catalog";
}

/// <summary>
/// Publishes Catalog's domain events (ADR-0003).
///
/// Publishing is deliberately best-effort: a Book being stored is the outcome the reader
/// asked for, and failing to announce it must not fail their search. That does mean an event
/// can be lost if the broker is unreachable at the wrong moment — making this at-most-once.
/// A system that needed every event would write it to an outbox table in the same
/// transaction as the Book and relay it separately.
/// </summary>
public sealed class RabbitMqBookEvents(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqBookEvents> logger) : IBookEvents, IAsyncDisposable
{
    public const string RoutingKey = "book.cached";

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connecting = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(BookCached bookCached, CancellationToken cancellationToken)
    {
        try
        {
            var channel = await OpenChannelAsync(cancellationToken);

            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: RoutingKey,
                body: JsonSerializer.SerializeToUtf8Bytes(bookCached),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not announce that Book {BookId} was cached; the Book itself was stored",
                bookCached.BookId);
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

            // Topic, so later services can subscribe to the slices of Catalog they care about.
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
