using DocumentIntelligence.Contracts.Messaging;
using DocumentService.Api.Messaging;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Endpoints.AnalyzeDocument;
using DocumentService.Api.Endpoints.GetDocumentResult;
using DocumentService.Api.Endpoints.Internal;
using DocumentService.Api.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

// 🔹 EF Core DbContext registrieren
builder.Services.AddDbContext<DocumentApiDbContext>(options =>
{
    // InMemory (für lokale Tests)
    options.UseInMemoryDatabase("DocumentApiDb");
    
    // Prod/Cloud später: 
    // options.UseSqlServer(builder.Configuration.GetConnectionString("DocumentApiDb"));
});

// Repo
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();

// Messaging
builder.Services.AddSingleton<IMessageBus, AzureServiceBusPublisher>();

// Handler, der die DB updated
builder.Services.AddScoped<AnalysisCompletedEventHandler>();

// Background Listener für das Event
builder.Services.AddHostedService<AnalysisCompletedEventConsumer>();



builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// internal auth for worker callback
app.UseAuthorization();

// map vertical slices
AnalyzeDocumentEndpoint.Map(app);
PostAnalysisResultEndpoint.Map(app);
GetDocumentResultEndpoint.Map(app);

app.Run();
