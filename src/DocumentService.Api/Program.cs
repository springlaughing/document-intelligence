using DocumentIntelligence.Contracts.Messaging;
using DocumentService.Api.Messaging;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Endpoints.AnalyzeDocument;
using DocumentService.Api.Endpoints.GetDocumentResult;
using DocumentService.Api.Endpoints.Internal;


var builder = WebApplication.CreateBuilder(args);

// Repo
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();

// Messaging
builder.Services.AddSingleton<IMessageBus, AzureServiceBusPublisher>();

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
