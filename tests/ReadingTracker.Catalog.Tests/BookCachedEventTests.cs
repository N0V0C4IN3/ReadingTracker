using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ReadingTracker.Catalog.Tests;

[Collection(CatalogApiCollection.Name)]
public sealed class BookCachedEventTests(CatalogApiFixture fixture)
{
    private const string Exchange = "readingtracker.catalog";

    [Fact]
    public async Task Announces_a_book_the_first_time_it_is_cached_but_not_again()
    {
        const string isbn = "9780345391803";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json($$"""
            {
              "kind": "books#volumes",
              "totalItems": 1,
              "items": [
                {
                  "id": "hitchhiker-1",
                  "volumeInfo": {
                    "title": "The Hitchhiker's Guide to the Galaxy",
                    "authors": ["Douglas Adams"],
                    "industryIdentifiers": [{ "type": "ISBN_13", "identifier": "{{isbn}}" }],
                    "pageCount": 224
                  }
                }
              ]
            }
            """);

        await using var listener = await EventListener.StartAsync(fixture.RabbitMqConnectionString);
        var client = fixture.CreateClient();

        var results = (await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results;
        var cachedBookId = Assert.Single(results).Id;

        var announced = await listener.NextAsync();
        Assert.Equal(cachedBookId, announced.BookId);
        Assert.Equal("The Hitchhiker's Guide to the Galaxy", announced.Title);
        Assert.Equal(isbn, announced.Isbn);
        Assert.Equal("GoogleBooks", announced.Source);

        // Searching again serves the same Book from cache, which is not news.
        await client.GetAsync($"/api/books/search?isbn={isbn}");

        Assert.Null(await listener.NextOrNullAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Announces_a_book_that_was_added_by_hand()
    {
        await using var listener = await EventListener.StartAsync(fixture.RabbitMqConnectionString);

        var response = await fixture.CreateClient().PostAsJsonAsync("/api/books", new
        {
            title = "A Book Only I Own",
            authors = new[] { "A. Writer" },
            isbn = "9783333333335",
        });
        var created = await response.Content.ReadFromJsonAsync<Book>();

        var announced = await listener.NextAsync();
        Assert.Equal(created!.Id, announced.BookId);
        Assert.Equal("Manual", announced.Source);
    }

    /// <summary>Subscribes to Catalog's exchange the way another service would.</summary>
    private sealed class EventListener : IAsyncDisposable
    {
        private readonly Channel<BookCachedMessage> _received =
            Channel.CreateUnbounded<BookCachedMessage>();

        private IConnection? _connection;
        private IChannel? _channel;

        public static async Task<EventListener> StartAsync(string connectionString)
        {
            var listener = new EventListener();
            var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

            listener._connection = await factory.CreateConnectionAsync();
            listener._channel = await listener._connection.CreateChannelAsync();

            await listener._channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true);
            var queue = await listener._channel.QueueDeclareAsync();
            await listener._channel.QueueBindAsync(queue.QueueName, Exchange, "book.#");

            var consumer = new AsyncEventingBasicConsumer(listener._channel);
            consumer.ReceivedAsync += (_, delivery) =>
            {
                var message = JsonSerializer.Deserialize<BookCachedMessage>(
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

        public async Task<BookCachedMessage> NextAsync() =>
            await NextOrNullAsync(TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("No event arrived on Catalog's exchange.");

        public async Task<BookCachedMessage?> NextOrNullAsync(TimeSpan within)
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

    public sealed record BookCachedMessage(Guid BookId, string Title, string? Isbn, string Source);

    private sealed record SearchResponse(IReadOnlyList<Book> Results);

    private sealed record Book(Guid Id, string Title);
}
