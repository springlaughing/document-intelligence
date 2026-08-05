using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocumentService.Api.Infrastructure.Ef;

// Used only by `dotnet ef` at design time. Without it the tooling has to boot the web
// host to find a DbContext, which would demand Service Bus configuration that scaffolding
// a migration has no business needing.
//
// Migrations are generated from the model, not from the database, so this connection
// string never has to point at a reachable server.
public class DocumentApiDbContextFactory : IDesignTimeDbContextFactory<DocumentApiDbContext>
{
    private const string DesignTimeFallback =
        "Server=localhost,1433;Database=DocumentDb;User Id=sa;Password=DesignTimeOnly!1;TrustServerCertificate=True";

    public DocumentApiDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DocumentDb")
            ?? DesignTimeFallback;

        var options = new DbContextOptionsBuilder<DocumentApiDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new DocumentApiDbContext(options);
    }
}
