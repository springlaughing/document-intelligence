using System;
using DocumentService.Api.Domain;

namespace DocumentService.Api.Infrastructure.Ef.Entities;

public class DocumentEntity
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = default!;
    public DocumentStatus Status { get; set; }

    public string? AnalysisSummary { get; set; }
    public string[]? ExtractedEntities { get; set; }
    public string? AnalysisBlobRef { get; set; }

    // When the document most recently entered Analyzing - the *last* attempt, not the
    // first. The reconciliation sweep measures staleness from here, so a document it
    // re-queues gets a full fresh window instead of being swept again on the next pass.
    public DateTimeOffset? AnalysisStartedAtUtc { get; set; }

    // How many times analysis has been requested, counting the original request. Only the
    // sweep increments it; a new request from a user resets it, because that is a new
    // analysis rather than another go at the old one.
    public int AnalysisAttempts { get; set; }

    // Why the document ended up Failed, when anything knows. The sweep sets it when it
    // gives up. An analysis failure reported by the worker still leaves it null - that
    // path carries its reason in the event, not the row.
    public string? FailureReason { get; set; }

    // Concurrency token (optimistic concurrency)
    public byte[]? RowVersion { get; set; } = default!;
}

