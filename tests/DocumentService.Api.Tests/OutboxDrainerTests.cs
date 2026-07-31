using DocumentIntelligence.Messaging;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using DocumentService.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

public class OutboxDrainerTests
{
    private static DocumentApiDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<DocumentApiDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static OutboxMessage Pending(string payload = "{\"documentId\":\"x\"}") => new()
    {
        Id = Guid.NewGuid(),
        EntityName = "analyze-document",
        MessageType = "AnalyzeDocumentCommand",
        Payload = payload,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Pending_messages_are_published_and_marked_sent()
    {
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        var msg = Pending();
        seed.OutboxMessages.Add(msg);
        await seed.SaveChangesAsync();

        var publisher = new Mock<IMessagePublisher>();
        var db = NewDb(dbName);
        var sut = new OutboxDrainer(db, publisher.Object, Mock.Of<ILogger<OutboxDrainer>>());

        var sent = await sut.DrainAsync(batchSize: 10);

        Assert.Equal(1, sent);
        publisher.Verify(p => p.PublishRawAsync(
            "analyze-document", msg.Payload, "AnalyzeDocumentCommand", It.IsAny<CancellationToken>()),
            Times.Once);

        var stored = await NewDb(dbName).OutboxMessages.SingleAsync();
        Assert.NotNull(stored.SentAtUtc);
        Assert.Null(stored.LastError);
    }

    [Fact]
    public async Task A_failed_publish_leaves_the_message_pending_for_the_next_pass()
    {
        // The reason the outbox exists: losing the broker must not lose the command.
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        seed.OutboxMessages.Add(Pending());
        await seed.SaveChangesAsync();

        var publisher = new Mock<IMessagePublisher>();
        publisher.Setup(p => p.PublishRawAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service bus unreachable"));

        var sut = new OutboxDrainer(NewDb(dbName), publisher.Object, Mock.Of<ILogger<OutboxDrainer>>());

        var sent = await sut.DrainAsync(batchSize: 10);

        Assert.Equal(0, sent);

        var stored = await NewDb(dbName).OutboxMessages.SingleAsync();
        Assert.Null(stored.SentAtUtc);                       // still pending
        Assert.Equal(1, stored.Attempts);                    // and the attempt is recorded
        Assert.Contains("unreachable", stored.LastError!);
    }

    [Fact]
    public async Task A_retry_after_a_failure_succeeds_and_clears_the_error()
    {
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        seed.OutboxMessages.Add(Pending());
        await seed.SaveChangesAsync();

        var publisher = new Mock<IMessagePublisher>();
        publisher.SetupSequence(p => p.PublishRawAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"))
            .Returns(Task.CompletedTask);

        await new OutboxDrainer(NewDb(dbName), publisher.Object, Mock.Of<ILogger<OutboxDrainer>>())
            .DrainAsync(batchSize: 10);
        var second = await new OutboxDrainer(NewDb(dbName), publisher.Object, Mock.Of<ILogger<OutboxDrainer>>())
            .DrainAsync(batchSize: 10);

        Assert.Equal(1, second);

        var stored = await NewDb(dbName).OutboxMessages.SingleAsync();
        Assert.NotNull(stored.SentAtUtc);
        Assert.Null(stored.LastError);
        Assert.Equal(1, stored.Attempts);   // the failed attempt is still on the record
    }

    [Fact]
    public async Task Already_sent_messages_are_not_published_again()
    {
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        var msg = Pending();
        msg.SentAtUtc = DateTimeOffset.UtcNow;
        seed.OutboxMessages.Add(msg);
        await seed.SaveChangesAsync();

        var publisher = new Mock<IMessagePublisher>();
        var sent = await new OutboxDrainer(NewDb(dbName), publisher.Object, Mock.Of<ILogger<OutboxDrainer>>())
            .DrainAsync(batchSize: 10);

        Assert.Equal(0, sent);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Oldest_messages_are_published_first()
    {
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);

        var older = Pending("{\"n\":1}");
        older.CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        var newer = Pending("{\"n\":2}");
        newer.CreatedAtUtc = DateTimeOffset.UtcNow;

        seed.OutboxMessages.AddRange(newer, older);   // deliberately out of order
        await seed.SaveChangesAsync();

        var order = new List<string>();
        var publisher = new Mock<IMessagePublisher>();
        publisher.Setup(p => p.PublishRawAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, payload, _, _) => order.Add(payload))
            .Returns(Task.CompletedTask);

        await new OutboxDrainer(NewDb(dbName), publisher.Object, Mock.Of<ILogger<OutboxDrainer>>())
            .DrainAsync(batchSize: 10);

        Assert.Equal(new[] { "{\"n\":1}", "{\"n\":2}" }, order);
    }
}
