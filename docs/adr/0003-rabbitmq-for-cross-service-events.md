# RabbitMQ for cross-service events, alongside synchronous REST

Most inter-service calls are synchronous REST through the Gateway. RabbitMQ is added specifically to carry domain events between services, scoped to two concrete events rather than an open-ended eventing style: `ReadingStatusChanged` (published by Library when a LibraryEntry's status changes) and `BookCached` (published by Catalog when it persists a new Book). This was a deliberate choice to demonstrate event-driven communication without forcing every interaction into an artificial async shape.
