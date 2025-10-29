using System;
using DocumentIntelligence.Contracts.DomainContracts;

namespace DocumentService.Api.Infrastructure.Ef.Entities;

public class DocumentEntity
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = default!;
    public DocumentStatus Status { get; set; }

    public string? AnalysisSummary { get; set; }
    public string[]? ExtractedEntities { get; set; }
    public string? AnalysisBlobRef { get; set; }

    // Concurrency token (optimistic concurrency)
    public byte[] RowVersion { get; set; } = default!;
}

