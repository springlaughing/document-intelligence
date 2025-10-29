using AnalysisService.Worker.Messaging;
using AnalysisService.Worker.Infrastructure;
using Azure.Messaging.ServiceBus; 


var builder = Host.CreateApplicationBuilder(args);


// read conn string from config
var serviceBusConnectionString =
    builder.Configuration.GetSection("AzureServiceBus")["ConnectionString"]
    ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString not configured");

// register ServiceBusClient as singleton
builder.Services.AddSingleton<ServiceBusClient>(sp =>
    new ServiceBusClient(serviceBusConnectionString));

// register publisher that uses ServiceBusClient
builder.Services.AddSingleton<IAnalysisResultEventPublisher, ServiceBusAnalysisResultEventPublisher>();

// 2. BackgroundService, der Azure Service Bus konsumiert
// (oder Emulator - gleicher Code, andere ConnectionString)
builder.Services.AddHostedService<AzureServiceBusConsumer>();

builder.Services.AddSingleton<IBlobWriter, BlobWriter>(); // or FakeBlobWriter
//Publish AnalysisCompletedEvent (with blobRef) back to the bus.
builder.Services.AddSingleton<IAnalysisResultEventPublisher, ServiceBusAnalysisResultEventPublisher>();


var host = builder.Build();
await host.RunAsync();

