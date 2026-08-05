using DocumentIntelligence.Contracts;
using DocumentService.Api.Domain;
using DocumentService.Api.Features.Documents.RequestAnalysis;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

// The sweep is the component that notices when nothing else did, so the cases that matter
// are the ones where it must do nothing: a document that is merely slow, one that has
// already finished, one another replica has taken. A sweep that is too eager manufactures
// exactly the duplicate work the rest of the pipeline exists to absorb.
public class StuckAnalysisReconcilerTests
{
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 3;
    private const int BatchSize = 20;

    private static DocumentApiDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<DocumentApiDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static StuckAnalysisReconciler NewReconciler(DocumentApiDbContext db)
    {
        var repo = new EfDocumentRepository(db, Mock.Of<ILogger<EfDocumentRepository>>());

        var queue = new Mock<IAnalyzeDocumentCommandQueue>();
        queue.Setup(q => q.Prepare(It.IsAny<AnalyzeDocumentCommand>()))
             .Returns(new OutboxEnqueue("analyze-document", nameof(AnalyzeDocumentCommand), "{}"));

        return new StuckAnalysisReconciler(
            repo, queue.Object, Mock.Of<ILogger<StuckAnalysisReconciler>>());
    }

    private static async Task<string> GivenDocument(
        DocumentStatus status, TimeSpan startedAgo, int attempts)
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = NewDb(dbName);

        db.Documents.Add(new DocumentEntity
        {
            Id = Guid.NewGuid(),
            FileName = "invoice.pdf",
            Status = status,
            AnalysisStartedAtUtc = DateTimeOffset.UtcNow - startedAgo,
            AnalysisAttempts = attempts
        });
        await db.SaveChangesAsync();

        return dbName;
    }

    [Fact]
    public async Task A_document_stuck_past_the_threshold_is_queued_again()
    {
        var dbName = await GivenDocument(DocumentStatus.Analyzing, TimeSpan.FromHours(1), attempts: 1);

        var pass = await NewReconciler(NewDb(dbName))
            .RunAsync(StuckAfter, MaxAttempts, BatchSize);

        Assert.Equal(1, pass.Requeued);
        Assert.Equal(0, pass.Abandoned);

        await using var db = NewDb(dbName);

        // The command must actually be queued, not merely counted.
        Assert.Equal(1, await db.OutboxMessages.CountAsync(
            m => m.MessageType == nameof(AnalyzeDocumentCommand)));

        var document = await db.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Analyzing, document.Status);
        Assert.Equal(2, document.AnalysisAttempts);

        // The clock restarts, or the very next pass would sweep it again.
        Assert.True(document.AnalysisStartedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task A_document_still_inside_the_threshold_is_left_alone()
    {
        // Slow is not stuck. This is the assertion that stops the sweep inventing work.
        var dbName = await GivenDocument(DocumentStatus.Analyzing, TimeSpan.FromMinutes(5), attempts: 1);

        var pass = await NewReconciler(NewDb(dbName))
            .RunAsync(StuckAfter, MaxAttempts, BatchSize);

        Assert.Equal(0, pass.Total);

        await using var db = NewDb(dbName);
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
        Assert.Equal(1, (await db.Documents.SingleAsync()).AnalysisAttempts);
    }

    [Theory]
    [InlineData(DocumentStatus.Analyzed)]
    [InlineData(DocumentStatus.Failed)]
    [InlineData(DocumentStatus.Uploaded)]
    public async Task Documents_not_in_Analyzing_are_never_swept(DocumentStatus status)
    {
        // However old. Only Analyzing means "someone owes us an answer".
        var dbName = await GivenDocument(status, TimeSpan.FromDays(30), attempts: 1);

        var pass = await NewReconciler(NewDb(dbName))
            .RunAsync(StuckAfter, MaxAttempts, BatchSize);

        Assert.Equal(0, pass.Total);
        Assert.Equal(0, await NewDb(dbName).OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task A_document_that_exhausted_its_attempts_is_failed_with_a_reason()
    {
        var dbName = await GivenDocument(
            DocumentStatus.Analyzing, TimeSpan.FromHours(1), attempts: MaxAttempts);

        var pass = await NewReconciler(NewDb(dbName))
            .RunAsync(StuckAfter, MaxAttempts, BatchSize);

        Assert.Equal(1, pass.Abandoned);
        Assert.Equal(0, pass.Requeued);

        await using var db = NewDb(dbName);

        // Giving up must not queue another command - that would be the loop this limit
        // exists to break.
        Assert.Equal(0, await db.OutboxMessages.CountAsync());

        var document = await db.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Contains($"{MaxAttempts} attempts", document.FailureReason);
    }

    [Fact]
    public async Task A_document_with_no_start_time_is_not_swept()
    {
        // Null means "we do not know when this started", and re-queueing on a guess is
        // worse than waiting. The migration backfills existing rows so this stays a
        // theoretical case rather than a permanent blind spot.
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewDb(dbName))
        {
            seed.Documents.Add(new DocumentEntity
            {
                Id = Guid.NewGuid(),
                FileName = "legacy.pdf",
                Status = DocumentStatus.Analyzing,
                AnalysisStartedAtUtc = null,
                AnalysisAttempts = 0
            });
            await seed.SaveChangesAsync();
        }

        var pass = await NewReconciler(NewDb(dbName))
            .RunAsync(StuckAfter, MaxAttempts, BatchSize);

        Assert.Equal(0, pass.Total);
    }

    [Fact]
    public async Task A_candidate_another_writer_already_handled_is_counted_as_contended()
    {
        // Two replicas select the same row; one commits first. The loser must notice from
        // the attempt count and do nothing, rather than incrementing on top and reaching
        // the limit at twice the intended rate.
        var dbName = await GivenDocument(DocumentStatus.Analyzing, TimeSpan.FromHours(1), attempts: 1);

        await using var db = NewDb(dbName);
        var repo = new EfDocumentRepository(db, Mock.Of<ILogger<EfDocumentRepository>>());
        var documentId = (await db.Documents.AsNoTracking().SingleAsync()).Id;

        var first = await repo.TryRetryAnalysisAsync(
            documentId, expectedAttempts: 1, new OutboxEnqueue("q", "t", "{}"));

        // Same observation replayed - exactly what the second replica would present.
        var second = await repo.TryRetryAnalysisAsync(
            documentId, expectedAttempts: 1, new OutboxEnqueue("q", "t", "{}"));

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(2, (await NewDb(dbName).Documents.SingleAsync()).AnalysisAttempts);
    }

    [Fact]
    public async Task The_batch_size_bounds_one_pass()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewDb(dbName))
        {
            for (var i = 0; i < 5; i++)
                seed.Documents.Add(new DocumentEntity
                {
                    Id = Guid.NewGuid(),
                    FileName = $"doc-{i}.pdf",
                    Status = DocumentStatus.Analyzing,
                    AnalysisStartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                    AnalysisAttempts = 1
                });
            await seed.SaveChangesAsync();
        }

        var pass = await NewReconciler(NewDb(dbName))
            .RunAsync(StuckAfter, MaxAttempts, batchSize: 2);

        Assert.Equal(2, pass.Requeued);
        Assert.Equal(3, await NewDb(dbName).Documents.CountAsync(d => d.AnalysisAttempts == 1));
    }

    [Fact]
    public async Task Requesting_analysis_again_resets_the_attempt_count()
    {
        // A user asking again is a new analysis, not another go at the old one. Without
        // the reset, a document that was reconciled once would start its next life one
        // attempt from being abandoned.
        var dbName = Guid.NewGuid().ToString();
        var documentId = Guid.NewGuid();

        await using (var seed = NewDb(dbName))
        {
            seed.Documents.Add(new DocumentEntity
            {
                Id = documentId,
                FileName = "invoice.pdf",
                Status = DocumentStatus.Failed,
                AnalysisStartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                AnalysisAttempts = MaxAttempts,
                FailureReason = "gave up earlier"
            });
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb(dbName);
        var repo = new EfDocumentRepository(db, Mock.Of<ILogger<EfDocumentRepository>>());

        await repo.TryStartAnalysisAsync(
            documentId, DocumentStatus.Analyzing, new OutboxEnqueue("q", "t", "{}"));

        var document = await NewDb(dbName).Documents.SingleAsync();
        Assert.Equal(1, document.AnalysisAttempts);
        Assert.Null(document.FailureReason);
    }
}
