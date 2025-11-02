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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

    }
}

