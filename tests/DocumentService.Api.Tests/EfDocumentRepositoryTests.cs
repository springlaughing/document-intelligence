using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

// These are the first tests in the suite that load EF Core, which is what makes them
// worth having beyond their coverage: they fail loudly if the System.Text.Json version
// alignment in this project's csproj is ever dropped.
//
// Scope note: the InMemory provider is not relational. It ignores IsRowVersion, max
// lengths and unique constraints, so concurrency and constraint behaviour cannot be
// tested here - that needs a real provider.
public class EfDocumentRepositoryTests
{
    private static EfDocumentRepository NewRepository(out DocumentApiDbContext db)
    {
        db = new DocumentApiDbContext(
            new DbContextOptionsBuilder<DocumentApiDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        return new EfDocumentRepository(db, Mock.Of<ILogger<EfDocumentRepository>>());
    }

    [Fact]
    public async Task Created_document_can_be_read_back()
    {
        var repo = NewRepository(out _);
        var id = Guid.NewGuid();

        var created = await repo.CreateIfNotExistsAsync(id, "invoice.pdf");

        Assert.True(created);

        var record = await repo.GetAsync(id);

        Assert.NotNull(record);
        Assert.Equal("invoice.pdf", record!.FileName);
        Assert.Equal(DocumentStatus.Uploaded, record.Status);
        Assert.Null(record.AnalysisSummary);
    }

    [Fact]
    public async Task Missing_document_reads_back_as_null()
    {
        var repo = NewRepository(out _);

        Assert.Null(await repo.GetAsync(Guid.NewGuid()));
        Assert.False(await repo.ExistsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Status_transition_is_persisted()
    {
        var repo = NewRepository(out _);
        var id = Guid.NewGuid();
        await repo.CreateIfNotExistsAsync(id, "invoice.pdf");

        var updated = await repo.SetStatusAsync(id, DocumentStatus.Analyzing);

        Assert.True(updated);
        Assert.Equal(DocumentStatus.Analyzing, (await repo.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Setting_status_on_a_missing_document_reports_failure()
    {
        var repo = NewRepository(out _);

        Assert.False(await repo.SetStatusAsync(Guid.NewGuid(), DocumentStatus.Analyzing));
    }

    [Fact]
    public async Task Analysis_result_is_written_with_the_status_the_caller_chose()
    {
        var repo = NewRepository(out _);
        var id = Guid.NewGuid();
        await repo.CreateIfNotExistsAsync(id, "invoice.pdf");

        var applied = await repo.TryApplyAnalysisResultAsync(
            Guid.NewGuid(), id, "a summary", "blob://ref.json", DocumentStatus.Analyzed);

        Assert.True(applied);

        var record = await repo.GetAsync(id);

        Assert.Equal("a summary", record!.AnalysisSummary);
        Assert.Equal("blob://ref.json", record.AnalysisBlobRef);
        Assert.Equal(DocumentStatus.Analyzed, record.Status);
    }

    [Fact]
    public async Task Redelivering_the_same_event_changes_nothing()
    {
        // The at-least-once case: the broker hands us the same event twice.
        var repo = NewRepository(out _);
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await repo.CreateIfNotExistsAsync(id, "invoice.pdf");

        var first = await repo.TryApplyAnalysisResultAsync(
            eventId, id, "first result", "blob://first.json", DocumentStatus.Analyzed);

        var second = await repo.TryApplyAnalysisResultAsync(
            eventId, id, "SECOND result", "blob://second.json", DocumentStatus.Analyzed);

        Assert.True(first);
        Assert.False(second);

        var record = await repo.GetAsync(id);
        Assert.Equal("first result", record!.AnalysisSummary);      // untouched
        Assert.Equal("blob://first.json", record.AnalysisBlobRef);
    }

    [Fact]
    public async Task A_different_event_for_the_same_document_still_applies()
    {
        // Guarding on EventId must not block a genuine re-analysis.
        var repo = NewRepository(out _);
        var id = Guid.NewGuid();
        await repo.CreateIfNotExistsAsync(id, "invoice.pdf");

        await repo.TryApplyAnalysisResultAsync(
            Guid.NewGuid(), id, "first pass", "blob://a.json", DocumentStatus.Analyzed);

        var second = await repo.TryApplyAnalysisResultAsync(
            Guid.NewGuid(), id, "second pass", "blob://b.json", DocumentStatus.Analyzed);

        Assert.True(second);
        Assert.Equal("second pass", (await repo.GetAsync(id))!.AnalysisSummary);
    }

    [Fact]
    public async Task Completed_and_failed_guards_do_not_block_each_other()
    {
        // The inbox key is (event, handler), so one event reaching two different
        // handlers is not a duplicate.
        var repo = NewRepository(out _);
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await repo.CreateIfNotExistsAsync(id, "invoice.pdf");

        var completed = await repo.TryApplyAnalysisResultAsync(
            eventId, id, "s", "b", DocumentStatus.Analyzed);
        var failed = await repo.TryApplyAnalysisFailureAsync(
            eventId, id, DocumentStatus.Failed);

        Assert.True(completed);
        Assert.True(failed);
    }

    [Fact]
    public async Task Applying_to_an_unknown_document_reports_failure()
    {
        var repo = NewRepository(out _);

        Assert.False(await repo.TryApplyAnalysisResultAsync(
            Guid.NewGuid(), Guid.NewGuid(), "s", "b", DocumentStatus.Analyzed));
    }
}
