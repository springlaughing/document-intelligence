using DocumentIntelligence.Contracts.Messaging;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentService.Api.Messaging;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
using Azure.Messaging.ServiceBus; 


var builder = WebApplication.CreateBuilder(args);
// read conn string from config
var serviceBusConnectionString =
    builder.Configuration.GetSection("AzureServiceBus")["ConnectionString"]
    ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString not configured");

// register ServiceBusClient as singleton
builder.Services.AddSingleton<ServiceBusClient>(sp =>
    new ServiceBusClient(serviceBusConnectionString));
// 1. Add controllers (classic MVC controller style)
builder.Services.AddControllers();
// 2. AuthN / AuthZ
// For now you might add builder.Services.AddAuthentication(...); AddAuthorization(...);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
    options.AddPolicy("Reader", p => p.RequireRole("admin", "user"));
});

// 🔹 EF Core DbContext registrieren
builder.Services.AddDbContext<DocumentApiDbContext>(options =>
{
    
    // InMemory (für lokale Tests)
    options.UseInMemoryDatabase("DocumentApiDb");
    
    // Prod/Cloud später: 
    // options.UseSqlServer(builder.Configuration.GetConnectionString("DocumentApiDb"));
});

// Repo
builder.Services.AddScoped<IDocumentRepository, EfDocumentRepository>();

// Messaging
builder.Services.AddSingleton<IMessageBus, AzureServiceBusPublisher>();

// Handler, der die DB updated
builder.Services.AddScoped<AnalysisCompletedEventHandler>();

// Background Listener für das Event
builder.Services.AddHostedService<AnalysisCompletedEventConsumer>();




builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
// Seed initial data (dev/test only)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DocumentApiDbContext>();

    if (!db.Documents.Any())
    {
        var doc1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var doc2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        db.Documents.AddRange(
            new DocumentEntity
            {
                Id = doc1Id,
                FileName = "invoice-123.pdf",
                Status = DocumentStatus.Uploaded,
                AnalysisSummary = null,
                AnalysisBlobRef = null,
                RowVersion = Array.Empty<byte>() // in-memory provider won't enforce this anyway
            },
            new DocumentEntity
            {
                Id = doc2Id,
                FileName = "contract-foo.pdf",
                Status = DocumentStatus.Uploaded,
                AnalysisSummary = "This contract covers cooperation terms between Foo and Bar.",
                AnalysisBlobRef = null,
                RowVersion = Array.Empty<byte>()
            }
        );

        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// internal auth for worker callback
app.UseAuthorization();

// map vertical slices
//AnalyzeDocumentEndpoint.Map(app);
//PostAnalysisResultEndpoint.Map(app);
//GetDocumentResultEndpoint.Map(app);

app.MapControllers();


app.Run();
