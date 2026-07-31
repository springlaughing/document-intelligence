using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using DocumentService.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

public class MessageRetentionSweeperTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static DocumentApiDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<DocumentApiDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static MessageRetentionSweeper NewSweeper(DocumentApiDbContext db) =>
        new(db, Mock.Of<ILogger<MessageRetentionSweeper>>());

    [Fact]
    public async Task Old_inbox_rows_are_removed_and_recent_ones_kept()
    {
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        seed.ProcessedMessages.AddRange(
            new ProcessedMessage
            {
                MessageId = Guid.NewGuid(), Handler = "AnalysisCompleted",
                ProcessedAtUtc = DateTimeOffset.UtcNow.AddDays(-30)
            },
            new ProcessedMessage
            {
                MessageId = Guid.NewGuid(), Handler = "AnalysisCompleted",
                ProcessedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            });
        await seed.SaveChangesAsync();

        var removed = await NewSweeper(NewDb(dbName)).SweepAsync(Retention);

        Assert.Equal(1, removed);
        Assert.Equal(1, await NewDb(dbName).ProcessedMessages.CountAsync());
    }

    [Fact]
    public async Task Unsent_outbox_rows_are_never_removed_however_old()
    {
        // The outbox still owes these to the broker. Age is irrelevant; deleting one
        // would lose exactly the message the outbox exists to protect.
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        seed.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EntityName = "analyze-document",
            MessageType = "AnalyzeDocumentCommand",
            Payload = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-365),
            SentAtUtc = null
        });
        await seed.SaveChangesAsync();

        var removed = await NewSweeper(NewDb(dbName)).SweepAsync(Retention);

        Assert.Equal(0, removed);
        Assert.Equal(1, await NewDb(dbName).OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Sent_outbox_rows_past_retention_are_removed()
    {
        var dbName = Guid.NewGuid().ToString();
        var seed = NewDb(dbName);
        seed.OutboxMessages.AddRange(
            new OutboxMessage
            {
                Id = Guid.NewGuid(), EntityName = "q", MessageType = "t", Payload = "{}",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
                SentAtUtc = DateTimeOffset.UtcNow.AddDays(-30)
            },
            new OutboxMessage
            {
                Id = Guid.NewGuid(), EntityName = "q", MessageType = "t", Payload = "{}",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                SentAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
        await seed.SaveChangesAsync();

        var removed = await NewSweeper(NewDb(dbName)).SweepAsync(Retention);

        Assert.Equal(1, removed);
        Assert.Equal(1, await NewDb(dbName).OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task An_empty_sweep_does_nothing()
    {
        Assert.Equal(0, await NewSweeper(NewDb(Guid.NewGuid().ToString())).SweepAsync(Retention));
    }
}
