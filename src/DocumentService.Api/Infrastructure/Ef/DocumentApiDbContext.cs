using Microsoft.EntityFrameworkCore;
using DocumentService.Api.Infrastructure.Ef.Entities;
using DocumentService.Api.Infrastructure.Ef.Configurations;
using System.Reflection;

namespace DocumentService.Api.Infrastructure.Ef;

public class DocumentApiDbContext : DbContext
{
    public DocumentApiDbContext(DbContextOptions<DocumentApiDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    // The inbox. Lives in the same store as Documents so that recording a message as
    // handled commits in the same transaction as its effect.
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    // The outbox, here for the mirror-image reason: a message we intend to publish is
    // written in the same transaction as the change that justifies it.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

    }
}

