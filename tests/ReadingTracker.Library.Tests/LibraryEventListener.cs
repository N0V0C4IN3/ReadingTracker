using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ReadingTracker.Library.Tests;

public sealed record ReadingStatusChangedMessage(
    Guid LibraryEntryId,
    string ReaderId,
    Guid BookId,
    string PreviousStatus,
    string NewStatus);

/// <summary>Subscribes to Library's exchange the way another service would.</summary>
public sealed class LibraryEventListener : IAsyncDisposable
{
    private const string Exchange = "readingtracker.library";

    private readonly Channel<ReadingStatusChangedMessage> _received =
        Channel.CreateUnbounded<ReadingStatusChangedMessage>();

    private IConnection? _connection;
    private IChannel? _channel;

    public static async Task<LibraryEventListener> StartAsync(string connectionString)
    {
        var listener = new LibraryEventListener();
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

        listener._connection = await factory.CreateConnectionAsync();
        listener._channel = await listener._connection.CreateChannelAsync();

        await listener._channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true);
        var queue = await listener._channel.QueueDeclareAsync();
        await listener._channel.QueueBindAsync(queue.QueueName, Exchange, "reading-status.#");

        var consumer = new AsyncEventingBasicConsumer(listener._channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            var message = JsonSerializer.Deserialize<ReadingStatusChangedMessage>(
                delivery.Body.Span,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (message is not null)
            {
                listener._received.Writer.TryWrite(message);
            }

            return Task.CompletedTask;
        };

        await listener._channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer);
        return listener;
    }

    public async Task<ReadingStatusChangedMessage> NextAsync() =>
        await NextOrNullAsync(TimeSpan.FromSeconds(15))
        ?? throw new InvalidOperationException("No event arrived on Library's exchange.");

    public async Task<ReadingStatusChangedMessage?> NextOrNullAsync(TimeSpan within)
    {
        using var timeout = new CancellationTokenSource(within);

        try
        {
            return await _received.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
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
    }
}
